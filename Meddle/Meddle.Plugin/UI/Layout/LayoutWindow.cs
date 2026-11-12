using System.Numerics;
using Dalamud.Interface;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using Dalamud.Bindings.ImGui;
using FFXIVClientStructs.FFXIV.Client.System.Resource.Handle;
using Meddle.Plugin.Models;
using Meddle.Plugin.Export.Ue;
using Meddle.Utils.Export.Ue;
using Meddle.Plugin.Models.Composer;
using Meddle.Plugin.Models.Layout;
using Meddle.Plugin.Services;
using Meddle.Plugin.UI.Windows;
using Meddle.Plugin.Utils;
using Meddle.SqPack;
using Microsoft.Extensions.Logging;

namespace Meddle.Plugin.UI.Layout;

public partial class LayoutWindow : ITab
{
    private readonly ComposerFactory composerFactory;
    private readonly UeInstanceExportService ueInstanceExportService;
    private bool exportUeAssets;
    private int ueMeshMergeMode;
    private readonly MdlMaterialWindowManager mdlMaterialWindowManager;
    private readonly IFramework framework;
    private readonly Configuration config;
    private readonly SqPack.SqPack dataManager;

    private readonly FileDialogManager fileDialog = new()
    {
        AddedWindowFlags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoDocking
    };

    private readonly LayoutService layoutService;
    private readonly ILogger<LayoutWindow> log;
    // private readonly Dictionary<string, MdlFile?> mdlCache = new();
    // private readonly Dictionary<string, MtrlFile?> mtrlCache = new();
    // private readonly Dictionary<string, ShpkFile?> shpkCache = new();
    private readonly ResolverService resolverService;
    private readonly Dictionary<nint, ParsedInstance> selectedInstances = new();
    private readonly SigUtil sigUtil;
    private readonly TextureCache textureCache;
    private readonly ITextureProvider textureProvider;
    private CancellationTokenSource cancelToken = new();
    private ParsedInstance[] currentLayout = [];
    private Vector3 searchOrigin;
    private Vector3 playerPosition;
    private Action? drawExportSettingsCallback;
    private Task exportTask = Task.CompletedTask;
    private ProgressWrapper? progress;

    private string? lastError;
    private string search = "";
    private bool requestedPopup;

    public LayoutWindow(
        LayoutService layoutService,
        Configuration config,
        SigUtil sigUtil,
        ILogger<LayoutWindow> log,
        TextureCache textureCache,
        ITextureProvider textureProvider,
        ResolverService resolverService,
        ComposerFactory composerFactory,
        MdlMaterialWindowManager mdlMaterialWindowManager,
        IFramework framework,
        SqPack.SqPack dataManager,
        UeInstanceExportService ueInstanceExportService)
    {
        this.layoutService = layoutService;
        this.config = config;
        this.sigUtil = sigUtil;
        this.log = log;
        this.textureCache = textureCache;
        this.textureProvider = textureProvider;
        this.resolverService = resolverService;
        this.composerFactory = composerFactory;
        this.mdlMaterialWindowManager = mdlMaterialWindowManager;
        this.framework = framework;
        this.dataManager = dataManager;
        this.ueInstanceExportService = ueInstanceExportService;
    }


    public string Name => Loc.Text("Layout");
    public int Order => (int)WindowOrder.Layout;
    public MenuType MenuType => MenuType.Default;

    public void Draw()
    {
        try
        {
            UiUtil.DrawProgress(exportTask, progress, cancelToken);

            SetupCurrentState();
            if (config.LayoutConfig.DrawOverlay)
            {
                DrawOverlayWindow(out _, out var selected);
                foreach (var group in selected)
                {
                    selectedInstances[group.Id] = group;
                }
            }

            DrawOptions();

            if (ImGui.CollapsingHeader(Loc.Label("Selected")))
            {
                DrawSelected();
            }

            if (ImGui.CollapsingHeader(Loc.Label("All")))
            {
                DrawAll();
            }

            fileDialog.Draw();

            if (requestedPopup)
            {
                ImGui.OpenPopup("ExportSettingsPopup");
                requestedPopup = false;
            }

            if (drawExportSettingsCallback != null)
            {
                drawExportSettingsCallback();
            }
        }
        catch (Exception e)
        {
            if (e.ToString() != lastError)
            {
                lastError = e.ToString();
                log.LogError(e, "Failed to draw Layout window");
            }

            ImGui.TextColored(new Vector4(1, 0, 0, 1), Loc.Text("FailedToDrawLayoutWindow"));
            ImGui.TextWrapped(e.ToString());
        }
    }
    private void DrawAll()
    {
        ImGui.InputText(Loc.Label("Search"), ref search, 100);

        using var id = ImRaii.PushId("layoutTable");
        var set = currentLayout
                  .Where(x =>
                  {
                      // keep regardless of distance
                      if (x is ParsedTerrainInstance) return true;
                      if (x is ParsedCameraInstance) return true;
                      if (x is ParsedEnvLightInstance) return true;
                      if (Vector3.Distance(x.Transform.Translation, searchOrigin) < config.LayoutConfig.WorldCutoffDistance)
                      {
                          return true;
                      }
                      
                      if (config.LayoutConfig.IncludeSharedGroupsWhereSubItemsAreWithinRange &&
                          x is ParsedSharedInstance sharedInstance &&
                          sharedInstance.Flatten().Any(i => Vector3.Distance(i.Transform.Translation, searchOrigin) < config.LayoutConfig.WorldCutoffDistance))
                      {
                          return true;
                      }

                      return false;
                  })
                  .Where(x => config.LayoutConfig.DrawTypes.HasFlag(x.Type));
        if (config.LayoutConfig.OrderByDistance)
        {
            set = set.OrderBy(x => Vector3.Distance(x.Transform.Translation, searchOrigin));
        }

        if (config.LayoutConfig.HideOffscreenCharacters)
        {
            set = set.Where(x => x is not ParsedCharacterInstance {Visible: false});
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            set = set.Where(x =>
            {
                if (x is ISearchableInstance si)
                {
                    return si.Search(search);
                }

                return true;
            });
        }

        var items = set.ToArray();


        var count = 0;
        foreach (var item in items)
        {
            if (item is ParsedSharedInstance shared)
            {
                count += shared.Flatten().Length;
            }
            else
            {
                count++;
            }
        }

        ExportButton(Loc.LabelFormat("ExportInstances", items.Length), items);
        ImGui.SameLine();
        if (ImGui.Button(Loc.LabelFormat("AddInstances", items.Length)))
        {
            foreach (var item in items)
            {
                selectedInstances[item.Id] = item;
            }
        }
        ImGui.SameLine();
        ImGui.Text(Loc.Format("Total", count));

        DrawInstanceTable(items, DrawLayoutButtons);
    }
    private void DrawSelected()
    {
        if (selectedInstances.Count == 0)
        {
            ImGui.Text(Loc.Text("NoInstancesSelected"));
        }

        using var disabled = ImRaii.Disabled(selectedInstances.Count == 0);
        using var id = ImRaii.PushId("selectedTable");
        ExportButton(Loc.LabelFormat("ExportInstances", selectedInstances.Count), selectedInstances.Values);

        ImGui.SameLine();
        if (ImGui.Button(Loc.Label("ClearAll")))
        {
            selectedInstances.Clear();
        }

        DrawInstanceTable(selectedInstances.Values.ToArray(), DrawSelectedButtons);
    }

    public void Dispose()
    {
        cancelToken.Cancel();
    }

    private unsafe void SetupCurrentState()
    {
        currentLayout = layoutService.LastState ?? [];
        playerPosition = sigUtil.GetLocalPosition();
        if (config.LayoutConfig.OriginAdjustment == OriginAdjustment.Player)
        {
            searchOrigin = playerPosition;
        }
        else if (config.LayoutConfig.OriginAdjustment == OriginAdjustment.Camera)
        {
            searchOrigin = sigUtil.GetCamera()->Position;
        }
        else if (config.LayoutConfig.OriginAdjustment == OriginAdjustment.Origin)
        {
            searchOrigin = Vector3.Zero;
        }
        else
        {
            searchOrigin = Vector3.Zero;
        }
        var requestTime = DateTime.UtcNow;
        framework.RunOnTick(() =>
        {
            layoutService.UpdateState(searchOrigin, requestTime);
        });
    }
    
    private void DrawExportSingle(ParsedInstance instance)
    {
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            ExportButton(FontAwesomeIcon.FileExport.ToIconString(), [instance]);
        }
    }

    private void DrawSelectedButtons(Stack<ParsedInstance> stack, ParsedInstance instance)
    {
        DrawExportSingle(instance);
        if (instance is IResolvableInstance {IsResolved: false} resolvableInstance)
        {
            ImGui.SameLine();
            using (ImRaii.PushFont(UiBuilder.IconFont))
            {
                if (ImGui.Button(FontAwesomeIcon.Redo.ToIconString()))
                {
                    resolvableInstance.Resolve(resolverService);
                }
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(Loc.Text("ResolveThisInstance"));
            }
        }

        if (stack.Count > 1)
        {
            return;
        }

        ImGui.SameLine();
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            if (ImGui.Button(FontAwesomeIcon.Trash.ToIconString()))
            {
                selectedInstances.Remove(instance.Id);
            }
        }
    }

    private void DrawLayoutButtons(Stack<ParsedInstance> stack, ParsedInstance instance)
    {
        DrawExportSingle(instance);
        
        if (config.DisplayDebugInfo && instance is ParsedBgPartsInstance {ModelPtr: not null} bg)
        {
            // open mdl/material window
            ImGui.SameLine();
            using (ImRaii.PushFont(UiBuilder.IconFont))
            {
                if (ImGui.Button(FontAwesomeIcon.PaintBrush.ToIconString()))
                {
                    unsafe
                    {
                        var pointer = (ModelResourceHandle*)bg.ModelPtr;
                        mdlMaterialWindowManager.AddMaterialWindow(pointer);
                    }
                }
            }
        }
        
        if (stack.Count > 1)
        {
            return;
        }

        ImGui.SameLine();
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            // if contains, refresh icon, if not, plus icon
            var icon = selectedInstances.ContainsKey(instance.Id) ? FontAwesomeIcon.Redo : FontAwesomeIcon.Plus;
            if (ImGui.Button(icon.ToIconString()))
            {
                selectedInstances[instance.Id] = instance;
            }
        }
    }

    private void ExportButton(string text, IEnumerable<ParsedInstance> instances)
    {
        using var disabled = ImRaii.Disabled(!exportTask.IsCompleted);
        if (ImGui.Button(text))
        {
            requestedPopup = true;
            var instancesArray = instances.ToArray();
            resolverService.ResolveInstances(instancesArray);
            var exportConfig = config.ExportConfig.Clone();
            exportConfig.SetDefaultCloneOptions();
            drawExportSettingsCallback = () => DrawExportSettings(instancesArray, exportConfig);
        }
    }

    private void DrawExportSettings(ParsedInstance[] instances, Configuration.ExportConfiguration exportConfig)
    {
        if (!exportTask.IsCompleted)
        {
            log.LogWarning("Export task already running");
            return;
        }

        if (!ImGui.BeginPopup("ExportSettingsPopup", ImGuiWindowFlags.AlwaysAutoResize)) return;
        try
        {
            var flags = UiUtil.ExportConfigDrawFlags.None;
            if (instances.Length == 1 && (instances[0].Type & ParsedInstanceType.Character) != 0)
            {
                flags |= UiUtil.ExportConfigDrawFlags.ShowExportPose;
                flags |= UiUtil.ExportConfigDrawFlags.ShowSubmeshOptions;
            }
            if (instances.Any(t => t is ParsedBgPartsInstance { IsVisible: false }))
            {
                flags |= UiUtil.ExportConfigDrawFlags.ShowBgPartOptions;
            }
            else if (instances.Any(t => t is ParsedSharedInstance sh && sh.Flatten().Any(i => i is ParsedBgPartsInstance { IsVisible: false })))
            {
                flags |= UiUtil.ExportConfigDrawFlags.ShowBgPartOptions;
            }
            if (instances.Any(t => (t.Type & ParsedInstanceType.Terrain) != 0))
            {
                flags |= UiUtil.ExportConfigDrawFlags.ShowTerrainOptions;
            }
            
            ImGui.Checkbox(Loc.Label("ExportUEAssets"), ref exportUeAssets);
            var drawFlags = exportUeAssets ? (flags & ~UiUtil.ExportConfigDrawFlags.ShowExportPose) | UiUtil.ExportConfigDrawFlags.NativeAssets : flags;
            if (UiUtil.DrawExportConfig(exportConfig, drawFlags))
            {
                config.ExportConfig.Apply(exportConfig);
                config.Save();
            }

            if (exportUeAssets)
            {
                ImGui.Combo(Loc.Label("ModelMerge"), ref ueMeshMergeMode, Loc.Text("ModelMergeChoices"));
                ImGui.TextWrapped(Loc.Text("LayoutExportHint"));
                if (!instances.Any(UeInstanceExportService.HasExportContent))
                    ImGui.TextWrapped(Loc.Text("NoExportableModels"));
            }

            using (ImRaii.Disabled(exportUeAssets && !instances.Any(UeInstanceExportService.HasExportContent)))
            {
                if (ImGui.Button(Loc.Label("Export")))
                {
                    exportConfig.UseDeformer = true;
                    if (!flags.HasFlag(UiUtil.ExportConfigDrawFlags.ShowExportPose))
                    {
                        // Force local pose mode if we don't show the pose option
                        exportConfig.PoseMode = SkeletonUtils.PoseMode.Local;
                    }

                    var filteredInstances = new List<ParsedInstance>();
                    foreach (var instance in instances)
                    {
                        // if instance has a parent, and the parent is also in the list, skip this instance since it will be exported as an attachment of the parent
                        if (instance is ParsedCharacterInstance {Parent: not null} childInstance)
                        {
                            var parentMatch = instances.FirstOrDefault(c => c.Id == childInstance.Parent.Id);
                            if (parentMatch != null)
                            {
                                string parentName;
                                if (parentMatch is ParsedCharacterInstance parentCharacter)
                                {
                                    parentName = parentCharacter.Name;
                                }
                                else
                                {
                                    parentName = parentMatch.Type.ToString();
                                }

                                log.LogDebug("Parented instance {type} {name} is already in the list as a child of {parentName}, skipping", childInstance.Kind, childInstance.Name, parentName);
                                continue;
                            }
                        }

                        filteredInstances.Add(instance);
                    }
                    
                    var filteredInstanceArray = filteredInstances.ToArray();
                    var ueExport = exportUeAssets;
                    var mergeMode = (UeMeshMergeMode)ueMeshMergeMode;

                    var defaultName = $"InstanceExport-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}";
                    cancelToken = new CancellationTokenSource();
                    fileDialog.SaveFolderDialog(Loc.Text("SaveInstances"), defaultName,
                                                (result, path) =>
                                                {
                                                    if (!result) return;
                                                    exportTask = Task.Run(() =>
                                                    {
                                                        progress = new ProgressWrapper();
                                                        if (ueExport)
                                                            ueInstanceExportService.Export(filteredInstanceArray, path, exportConfig, mergeMode, cancelToken.Token, progress);
                                                        else
                                                        {
                                                            var composer = composerFactory.CreateComposer(path, exportConfig, cancelToken.Token);
                                                            composer.Compose(filteredInstanceArray, progress);
                                                        }
                                                        ExportUtil.OpenExportFolderInExplorer(path, config, cancelToken.Token);
                                                    }, cancelToken.Token);
                                                }, config.ExportDirectory);

                    drawExportSettingsCallback = null;
                    ImGui.CloseCurrentPopup();
                }

            }
            ImGui.SameLine();
            if (ImGui.Button(Loc.Label("Cancel")))
            {
                drawExportSettingsCallback = null;
                ImGui.CloseCurrentPopup();
            }
        } 
        finally
        {
            ImGui.EndPopup();
        }
    }
}

public class ProgressWrapper
{
    public ExportProgress? Progress { get; set; }
}
