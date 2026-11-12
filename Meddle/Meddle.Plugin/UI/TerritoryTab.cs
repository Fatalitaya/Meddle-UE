using System.Numerics;
using System.Text.Json;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Plugin.Services;
using Meddle.Formats.Files;
using Meddle.Plugin.Export.Ue;
using Meddle.Plugin.Models;
using Meddle.Plugin.Models.Composer;
using Meddle.Plugin.Models.Layout;
using Meddle.Plugin.Services;
using Meddle.Plugin.Utils;
using Meddle.Utils.Export.Ue;
using Meddle.Utils.Export;
using Meddle.Plugin.UI.Layout;

namespace Meddle.Plugin.UI;

public sealed class TerritoryTab : ITab
{
    private readonly TerritoryExportService territoryExportService;
    private readonly LayoutService layoutService;
    private readonly Configuration config;
    private readonly IFramework framework;
    private readonly ComposerFactory composerFactory;
    private readonly UeInstanceExportService ueInstanceExportService;
    private readonly FileDialogManager fileDialog = new();
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource cancelToken = new();
    private Task exportTask = Task.CompletedTask;
    private ProgressWrapper? progress;
    private bool exportUeAssets;
    private bool exportUeLevel = true;
    private int groupMode = (int)TerritoryGroupMode.RepeatedResource;
    private Task<uint?>? currentTerritoryRequest;
    private bool exportCurrentRequested;
    private bool destinationPending;

    public TerritoryTab(TerritoryExportService territoryExportService, LayoutService layoutService,
        Configuration config, IFramework framework, ComposerFactory composerFactory, UeInstanceExportService ueInstanceExportService)
    {
        this.territoryExportService = territoryExportService;
        this.layoutService = layoutService;
        this.config = config;
        this.framework = framework;
        this.composerFactory = composerFactory;
        this.ueInstanceExportService = ueInstanceExportService;
    }

    public string Name => Loc.Text("Territory");
    public int Order => (int)WindowOrder.Territory;
    public MenuType MenuType => MenuType.Default;

    public void Draw()
    {
        try
        {
            UiUtil.DrawProgress(exportTask, progress, cancelToken);
            DrawTerritoryExport();
        }
        catch (Exception e) { territoryError = e.Message; ImGui.TextWrapped(e.Message); }
        fileDialog.Draw();
    }

    public void Dispose()
    {
        lifetime.Cancel();
        cancelToken.Cancel();
    }
    private TerritoryExportService.Territory[]? territoryChoices;
    private TerritoryExportService.Territory? selectedTerritory;
    private Task<TerritoryExportService.Document>? territoryLoad;
    private TerritoryExportService.Document? territoryDocument;
    private LayoutService.TerritorySnapshot? territoryPreview;
    private string territorySearch = "";
    private string? territoryError;
    private uint territoryFilter;
    // -2 = current live festival combination; -1 = no festival; >= 0 = an available festival phase.
    private int territoryFestival = -1;
    private TerritorySceneFile.Festival[] territoryFestivals = [];
    private bool territoryCurrentState;
    private Configuration.ExportConfiguration? territorySettings;
    private string? territorySummary;

    private void DrawTerritoryExport()
    {
        using var scope = ImRaii.PushId("territoryExport");
        territoryChoices ??= territoryExportService.GetTerritories();
        using (ImRaii.Disabled(!exportTask.IsCompleted || territoryLoad is { IsCompleted: false } || destinationPending || currentTerritoryRequest != null))
        {
            ImGui.Checkbox(Loc.Label("ExportUEAssets"), ref exportUeAssets);
            if (exportUeAssets) ImGui.Checkbox(Loc.Label("UeSublevel"), ref exportUeLevel);
            ImGui.Combo(Loc.Label("Grouping"), ref groupMode, Loc.Text("GroupingChoices"));
            ImGui.TextWrapped((TerritoryGroupMode)groupMode switch
            {
                TerritoryGroupMode.All => exportUeAssets && exportUeLevel ? Loc.Text("GroupAllLevel") : Loc.Text("GroupAll"),
                TerritoryGroupMode.RepeatedResource => Loc.Text("GroupResources"),
                TerritoryGroupMode.Layer => Loc.Text("GroupLayers"),
                _ => Loc.Text("GroupInstances")
            });
            ImGui.TextWrapped(exportUeAssets ? (exportUeLevel
                ? Loc.Text("LevelExportHint")
                : Loc.Text("BakedExportHint")) :
                Loc.Text("GltfExportHint"));
            if (ImGui.Button(Loc.Label("ExportCurrentTerritory")))
            {
                territoryError = null;
                currentTerritoryRequest = framework.RunOnTick(() => layoutService.CurrentTerritoryId());
            }
            ImGui.InputText(Loc.Label("TerritorySearch"), ref territorySearch, 200);
            if (ImGui.BeginCombo(Loc.Label("TerritoryChoice"), selectedTerritory?.Name ?? Loc.Text("ChooseTerritory")))
            {
                foreach (var territory in territoryChoices.Where(t => t.Matches(territorySearch)))
                {
                    if (!ImGui.Selectable(territory.Name, selectedTerritory?.Id == territory.Id)) continue;
                    selectedTerritory = territory;
                    territoryDocument = null;
                    territoryError = null;
                    territorySummary = null;
                    LoadTerritory(territory);
                }
                ImGui.EndCombo();
            }
        }
        if (currentTerritoryRequest is { IsCompleted: true })
        {
            try
            {
                var id = currentTerritoryRequest.GetAwaiter().GetResult();
                var territory = territoryChoices.FirstOrDefault(t => t.Id == id)
                    ?? throw new InvalidOperationException(Loc.Text("NoLoadedTerritory"));
                selectedTerritory = territory;
                territoryDocument = null;
                territorySummary = null;
                exportCurrentRequested = true;
                LoadTerritory(territory);
            }
            catch (Exception e) { territoryError = e.Message; exportCurrentRequested = false; }
            currentTerritoryRequest = null;
        }
        if (territoryLoad is { IsCompleted: false }) ImGui.TextUnformatted(Loc.Text("LoadingTerritory"));
        if (territoryLoad is { IsCompleted: true })
        {
            try
            {
                territoryDocument = territoryLoad.GetAwaiter().GetResult();
                territoryCurrentState = territoryPreview != null;
                territoryFilter = territoryDocument.Filters.FirstOrDefault(f => f.TerritoryId == territoryDocument.Territory.Id
                    && f.ContentId == (territoryPreview?.ContentId ?? 0))?.Key
                    ?? territoryDocument.Filters.FirstOrDefault(f => f.TerritoryId == territoryDocument.Territory.Id)?.Key ?? 0;
                territoryFestival = territoryPreview == null ? -1 : -2;
                RefreshTerritoryFestivals();
                territorySettings = config.ExportConfig.Clone();
                territorySettings.SetDefaultCloneOptions();
                territorySettings.LimitTerrainExportRange = false;
                if (exportCurrentRequested)
                {
                    if (territoryPreview == null) throw new InvalidOperationException(Loc.Text("TerritoryChanged"));
                    BeginTerritoryExport();
                }
            }
            catch (Exception e) { territoryError = e.Message; }
            territoryLoad = null;
            exportCurrentRequested = false;
        }
        if (territoryError != null) ImGui.TextWrapped(territoryError);
        if (territorySummary != null) ImGui.TextWrapped(territorySummary);
        if (territoryDocument == null || territorySettings == null) return;

        using var busy = ImRaii.Disabled(!exportTask.IsCompleted || territoryLoad is { IsCompleted: false } || destinationPending || currentTerritoryRequest != null);
        if (ImGui.Button(Loc.Label("RefreshTerritory"))) LoadTerritory(territoryDocument.Territory);
        ImGui.TextUnformatted(Loc.Format("TerritoryLayers", territoryDocument.Layers.Length, Loc.Text(territoryPreview == null ? "StaticTerritory" : "LiveTerritory")));
        if (territoryPreview != null && ImGui.Checkbox(Loc.Label("CurrentBranch"), ref territoryCurrentState))
        {
            territoryFestival = territoryCurrentState ? -2 : -1;
            if (territoryCurrentState)
                territoryFilter = territoryDocument.Filters.FirstOrDefault(f => f.TerritoryId == territoryPreview.TerritoryId
                    && f.ContentId == territoryPreview.ContentId)?.Key ?? territoryPreview.FilterKey;
            RefreshTerritoryFestivals();
        }
        using (ImRaii.Disabled(territoryCurrentState))
        {
            string FilterName(TerritorySceneFile.Filter f) =>
                Loc.Format("TerritoryFilter", territoryChoices.FirstOrDefault(t => t.Id == f.TerritoryId)?.Name ?? Loc.Text("Territory") + " " + f.TerritoryId, f.ContentId, f.Key);
            var selected = territoryDocument.Filters.FirstOrDefault(f => f.Key == territoryFilter);
            if (ImGui.BeginCombo(Loc.Label("SceneVersion"), selected == null ? Loc.Text("Default") : FilterName(selected)))
            {
                foreach (var filter in territoryDocument.Filters.DistinctBy(f => f.Key))
                {
                    if (!ImGui.Selectable(FilterName(filter) + "###Filter" + filter.Key, filter.Key == territoryFilter)) continue;
                    territoryFilter = filter.Key;
                    territoryFestival = -1;
                    RefreshTerritoryFestivals();
                }
                ImGui.EndCombo();
            }
            var festivalName = territoryFestival == -2 ? Loc.Text("CurrentFestivals") : territoryFestival < 0 ? Loc.Text("NoFestival") :
                territoryExportService.FestivalName(territoryFestivals[territoryFestival]);
            if (ImGui.BeginCombo(Loc.Label("Festival"), festivalName))
            {
                if (ImGui.Selectable(Loc.Label("NoFestival"), territoryFestival == -1)) territoryFestival = -1;
                if (territoryPreview != null && ImGui.Selectable(Loc.Label("CurrentFestivals"), territoryFestival == -2)) territoryFestival = -2;
                for (var i = 0; i < territoryFestivals.Length; i++)
                    if (ImGui.Selectable(territoryExportService.FestivalName(territoryFestivals[i]), territoryFestival == i)) territoryFestival = i;
                ImGui.EndCombo();
            }
        }
        ImGui.TextWrapped(Loc.Text("TerritoryHint"));
        UiUtil.DrawExportConfig(territorySettings, exportUeAssets ? UiUtil.ExportConfigDrawFlags.NativeAssets : UiUtil.ExportConfigDrawFlags.None);
        if (ImGui.Button(Loc.Label("ExportTerritory"))) BeginTerritoryExport();
    }

    private void LoadTerritory(TerritoryExportService.Territory territory)
    {
        territoryError = null;
        territoryLoad = Task.Run(async () =>
        {
            var document = territoryExportService.Load(territory, lifetime.Token);
            territoryPreview = await framework.RunOnTick(() => layoutService.CaptureTerritory(territory.Id,
                includeInstances: false, token: lifetime.Token), cancellationToken: lifetime.Token).ConfigureAwait(false);
            return document;
        });
    }

    private void RefreshTerritoryFestivals()
    {
        territoryFestivals = territoryDocument!.Layers.Select(l => l.Layer)
            .Where(l => l.Festival.Id != 0 && l.Matches(territoryFilter, new HashSet<TerritorySceneFile.Festival> { l.Festival }))
            .Select(l => l.Festival).Distinct().OrderBy(f => f.Id).ThenBy(f => f.Phase).ToArray();
    }

    private void BeginTerritoryExport()
    {
        var document = territoryDocument!;
        var settings = territorySettings!.Clone();
        settings.LimitTerrainExportRange = false;
        var ue = exportUeAssets;
        var ueLevel = ue && exportUeLevel;
        var grouping = (TerritoryGroupMode)groupMode;
        var currentState = territoryCurrentState;
        var festivalIndex = territoryFestival;
        var chosen = festivalIndex >= 0 ? territoryFestivals[festivalIndex] : default;
        var filter = territoryFilter;
        destinationPending = true;
        fileDialog.SaveFolderDialog(Loc.Text("ExportTerritory"), $"Territory-{document.Territory.Id}-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}", (accepted, destination) =>
        {
            destinationPending = false;
            if (!accepted) return;
            cancelToken = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            var token = cancelToken.Token;
            territoryError = null;
            territorySummary = null;
            exportTask = Task.Run(async () =>
            {
                try
                {
                    progress = new ProgressWrapper { Progress = new ExportProgress(1, Loc.Text("ReadTerritory")) };
                    var capture = currentState || festivalIndex == -2
                        ? framework.RunOnTick(() => layoutService.CaptureTerritory(document.Territory.Id,
                            includeInstances: currentState, token: token), cancellationToken: token)
                        : Task.FromResult<LayoutService.TerritorySnapshot?>(null);
                    await BackgroundExport.RunAfterCaptureAsync(capture, snapshot =>
                    {
                        token.ThrowIfCancellationRequested();
                        if (festivalIndex == -2 && snapshot == null)
                            throw new InvalidOperationException(Loc.Text("TerritoryChangedFestival"));
                        var festivals = festivalIndex == -2 ? snapshot!.Festivals : new HashSet<TerritorySceneFile.Festival>();
                        if (festivalIndex >= 0) festivals.Add(chosen);
                        var selection = new TerritoryExportService.Selection(filter, festivals, currentState);
                        var result = territoryExportService.Build(document, selection, snapshot, token);
                        var groups = territoryExportService.Group(result, grouping);
                        Directory.CreateDirectory(destination);
                        File.WriteAllText(Path.Combine(destination, "territory-report.json"), JsonSerializer.Serialize(result.Report, new JsonSerializerOptions { WriteIndented = true }));
                        File.WriteAllText(Path.Combine(destination, "territory-groups.json"), JsonSerializer.Serialize(new
                        {
                            Mode = grouping.ToString(),
                            Output = ueLevel ? "UeSublevel" : ue ? "UeBakedMeshes" : "Gltf",
                            Groups = groups.Select(g => new { g.Name, InstanceCount = g.Items.Length })
                        }, new JsonSerializerOptions { WriteIndented = true }));
                        var groupProgress = new ExportProgress(groups.Length, Loc.Text("TerritoryGroups"));
                        progress.Progress = groupProgress;
                        if (ueLevel)
                            ueInstanceExportService.ExportLevel(groups, destination, $"Territory_{document.Territory.Id}", settings, token, progress);
                        var composer = ue ? null : composerFactory.CreateComposer(destination, settings, token);
                        foreach (var group in ueLevel ? [] : groups)
                        {
                            token.ThrowIfCancellationRequested();
                            var name = $"Territory_{document.Territory.Id}_{group.Name}";
                            if (ue)
                            {
                                var root = new ParsedSharedInstance(nint.MinValue, new Transform(Vector3.Zero, Quaternion.Identity, Vector3.One),
                                    name, document.ScenePath, group.Items);
                                // Grouping has already been decided; bake all geometry within each group.
                                ueInstanceExportService.Export([root], destination, settings, UeMeshMergeMode.All, token, new ProgressWrapper());
                                groupProgress.IncrementProgress();
                                continue;
                            }
                            var scene = new SharpGLTF.Scenes.SceneBuilder();
                            var composeProgress = new ExportProgress(group.Items.Length, name);
                            groupProgress.Children.Add(composeProgress);
                            foreach (var instance in group.Items)
                            {
                                token.ThrowIfCancellationRequested();
                                if (composer!.ComposeInstance(instance, scene, composeProgress) == null)
                                    throw new InvalidDataException(Loc.Format("InstanceGeometryFailed", instance.Id));
                                composeProgress.IncrementProgress();
                            }
                            token.ThrowIfCancellationRequested();
                            ExportUtil.SaveAsType(scene.ToGltf2(), settings.ExportType, destination, name);
                            composeProgress.IsComplete = true;
                            groupProgress.IncrementProgress();
                        }
                        token.ThrowIfCancellationRequested();
                        var report = result.Report;
                        territorySummary = Loc.Format("TerritoryComplete", groups.Length, report.StaticModels, report.RuntimeModels, report.HousingModels, report.MissingResources.Count, report.UnmatchedRuntimeChildren);
                        ExportUtil.OpenExportFolderInExplorer(destination, config, token);
                    }, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { territorySummary = Loc.Text("TerritoryCancelled"); }
                catch (Exception e) { territoryError = Loc.Text("TerritoryFailed") + e.Message; throw; }
                finally { if (progress?.Progress != null) progress.Progress.IsComplete = true; }
            }, token);
        }, config.ExportDirectory);
    }
}
