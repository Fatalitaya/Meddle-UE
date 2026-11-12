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

    public string Name => "Territory";
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
            ImGui.Checkbox("Export UE Assets", ref exportUeAssets);
            ImGui.Combo("分组规则", ref groupMode, "全部合并\0按重复资源合并\0按场景层分组\0逐实例导出\0");
            ImGui.TextWrapped((TerritoryGroupMode)groupMode switch
            {
                TerritoryGroupMode.All => "整张地图一组。适合整体使用；大型区域会生成较大的网格。",
                TerritoryGroupMode.RepeatedResource => "模型完整路径、替换材质和染色相同的实例归为一组；保留所有摆放位置。地形块按各自资源分组。",
                TerritoryGroupMode.Layer => "按布局文件和场景层分组；嵌套物件归入所属顶层场景层，地形单独一组。",
                _ => "每个模型实例单独导出；SGB 展开为子模型，地形逐块导出。"
            });
            ImGui.TextWrapped(exportUeAssets ? "UE：每组烘焙为一个网格，保留世界位置；不会生成实例化组件。" :
                "glTF/GLB：每组一个场景文件，保留组内节点与世界位置。更改分组不会删除重复摆放。");
            if (ImGui.Button("直接导出当前区域…"))
            {
                territoryError = null;
                currentTerritoryRequest = framework.RunOnTick(() => layoutService.CurrentTerritoryId());
            }
            ImGui.InputText("关卡名称 / 代号 / ID", ref territorySearch, 200);
            if (ImGui.BeginCombo("区域", selectedTerritory?.Name ?? "选择 Territory"))
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
                    ?? throw new InvalidOperationException("当前没有可导出的已加载区域。");
                selectedTerritory = territory;
                territoryDocument = null;
                territorySummary = null;
                exportCurrentRequested = true;
                LoadTerritory(territory);
            }
            catch (Exception e) { territoryError = e.Message; exportCurrentRequested = false; }
            currentTerritoryRequest = null;
        }
        if (territoryLoad is { IsCompleted: false }) ImGui.TextUnformatted("正在读取区域布局…");
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
                    if (territoryPreview == null) throw new InvalidOperationException("区域已改变，请重新点击导出当前区域。");
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
        if (ImGui.Button("刷新区域状态")) LoadTerritory(territoryDocument.Territory);
        ImGui.TextUnformatted($"{territoryDocument.Layers.Length} 个场景层；{(territoryPreview == null ? "静态文件导出" : "当前区域：静态文件 + 运行时对照")}");
        if (territoryPreview != null && ImGui.Checkbox("使用当前游戏分支", ref territoryCurrentState))
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
                $"{territoryChoices.FirstOrDefault(t => t.Id == f.TerritoryId)?.Name ?? "区域 " + f.TerritoryId} / 内容 {f.ContentId} / 分支 {f.Key}";
            var selected = territoryDocument.Filters.FirstOrDefault(f => f.Key == territoryFilter);
            if (ImGui.BeginCombo("场景版本", selected == null ? "默认" : FilterName(selected)))
            {
                foreach (var filter in territoryDocument.Filters.DistinctBy(f => f.Key))
                {
                    if (!ImGui.Selectable(FilterName(filter), filter.Key == territoryFilter)) continue;
                    territoryFilter = filter.Key;
                    territoryFestival = -1;
                    RefreshTerritoryFestivals();
                }
                ImGui.EndCombo();
            }
            var festivalName = territoryFestival == -2 ? "当前节庆组合" : territoryFestival < 0 ? "无节庆" :
                territoryExportService.FestivalName(territoryFestivals[territoryFestival]);
            if (ImGui.BeginCombo("节庆布景 / 阶段", festivalName))
            {
                if (ImGui.Selectable("无节庆", territoryFestival == -1)) territoryFestival = -1;
                if (territoryPreview != null && ImGui.Selectable("当前节庆组合", territoryFestival == -2)) territoryFestival = -2;
                for (var i = 0; i < territoryFestivals.Length; i++)
                    if (ImGui.Selectable(territoryExportService.FestivalName(territoryFestivals[i]), territoryFestival == i)) territoryFestival = i;
                ImGui.EndCombo();
            }
        }
        ImGui.TextWrapped("导出完整区域，不受附近物件距离限制。手动选择节庆时，只对照目标分支共有的物件。动态机关使用已采集状态；未加载物件使用文件默认状态。");
        UiUtil.DrawExportConfig(territorySettings, exportUeAssets ? UiUtil.ExportConfigDrawFlags.NativeAssets : UiUtil.ExportConfigDrawFlags.None);
        if (ImGui.Button("导出 Territory")) BeginTerritoryExport();
    }

    private void LoadTerritory(TerritoryExportService.Territory territory)
    {
        territoryError = null;
        territoryLoad = Task.Run(async () =>
        {
            var document = territoryExportService.Load(territory, lifetime.Token);
            territoryPreview = await framework.RunOnTick(() => layoutService.CaptureTerritory(territory.Id));
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
        var grouping = (TerritoryGroupMode)groupMode;
        var currentState = territoryCurrentState;
        var festivalIndex = territoryFestival;
        var chosen = festivalIndex >= 0 ? territoryFestivals[festivalIndex] : default;
        var filter = territoryFilter;
        destinationPending = true;
        fileDialog.SaveFolderDialog("导出 Territory", $"Territory-{document.Territory.Id}-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}", (accepted, destination) =>
        {
            destinationPending = false;
            if (!accepted) return;
            cancelToken = new CancellationTokenSource();
            var token = cancelToken.Token;
            territoryError = null;
            territorySummary = null;
            exportTask = Task.Run(async () =>
            {
                try
                {
                    progress = new ProgressWrapper { Progress = new ExportProgress(1, "读取 Territory") };
                    var snapshot = await framework.RunOnTick(() => layoutService.CaptureTerritory(document.Territory.Id));
                    token.ThrowIfCancellationRequested();
                    if (festivalIndex == -2 && snapshot == null)
                        throw new InvalidOperationException("当前区域已改变，请重新选择区域或静态节庆分支。");
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
                        Groups = groups.Select(g => new { g.Name, InstanceCount = g.Items.Length })
                    }, new JsonSerializerOptions { WriteIndented = true }));
                    var groupProgress = new ExportProgress(groups.Length, "Territory 分组");
                    progress.Progress = groupProgress;
                    var composer = ue ? null : composerFactory.CreateComposer(destination, settings, token);
                    foreach (var group in groups)
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
                                throw new InvalidDataException($"无法生成实例 {instance.Id} 的几何体。");
                            composeProgress.IncrementProgress();
                        }
                        token.ThrowIfCancellationRequested();
                        ExportUtil.SaveAsType(scene.ToGltf2(), settings.ExportType, destination, name);
                        composeProgress.IsComplete = true;
                        groupProgress.IncrementProgress();
                    }
                    token.ThrowIfCancellationRequested();
                    var report = result.Report;
                    territorySummary = $"导出完成：{groups.Length} 组；静态模型 {report.StaticModels}，运行时模型 {report.RuntimeModels}，缺失资源 {report.MissingResources.Count}，未匹配子物件 {report.UnmatchedRuntimeChildren}。详情见 territory-report.json 和 territory-groups.json。";
                    ExportUtil.OpenExportFolderInExplorer(destination, config, token);
                }
                catch (OperationCanceledException) { territorySummary = "Territory 导出已取消。"; }
                catch (Exception e) { territoryError = "Territory 导出失败：" + e.Message; throw; }
                finally { if (progress?.Progress != null) progress.Progress.IsComplete = true; }
            }, token);
        }, config.ExportDirectory);
    }
}
