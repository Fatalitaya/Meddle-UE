using System.Numerics;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using Meddle.Formats.Files;
using Meddle.Plugin.Models;
using Meddle.Plugin.Models.Layout;
using Meddle.Utils.Export;
using static Meddle.Plugin.Services.LayoutService;

namespace Meddle.Plugin.Services;

public sealed class TerritoryExportService(IDataManager excel, SqPack.SqPack pack) : IService
{
    public record Territory(uint Id, string Name, string Bg)
    {
        public string Code => Path.GetFileNameWithoutExtension(Bg.Replace('\\', '/'));
        public bool Matches(string query) => Name.Contains(query, StringComparison.OrdinalIgnoreCase)
            || Code.Contains(query, StringComparison.OrdinalIgnoreCase) || Bg.Contains(query, StringComparison.OrdinalIgnoreCase);
    }
    public record SourceLayer(string Resource, TerritorySceneFile.Layer Layer);
    public record Document(Territory Territory, string ScenePath, string TerrainPath,
        SourceLayer[] Layers, TerritorySceneFile.Filter[] Filters);
    public record Selection(uint FilterKey, HashSet<TerritorySceneFile.Festival> Festivals, bool UseCurrentState);
    public sealed class Report
    {
        public uint TerritoryId { get; init; }
        public uint FilterKey { get; init; }
        public TerritorySceneFile.Festival[] Festivals { get; init; } = [];
        public bool ComparedWithRuntime { get; init; }
        public int StaticModels { get; set; }
        public int RuntimeModels { get; set; }
        public int MatchedRuntimeInstances { get; set; }
        public int InactiveInstances { get; set; }
        public int UnmatchedRuntimeChildren { get; set; }
        public int HousingRoots { get; set; }
        public int HousingModels { get; set; }
        public Dictionary<uint, int> UnsupportedTypes { get; } = [];
        public HashSet<string> MissingResources { get; } = [];
        public HashSet<string> Warnings { get; } = [];
    }
    public record Result(ParsedInstance[] Instances, Report Report, IReadOnlyDictionary<nint, string> Layers);

    public TerritoryExportGroup<ParsedInstance>[] Group(Result result, TerritoryGroupMode mode)
    {
        var entries = new List<TerritoryGroupItem<ParsedInstance>>();
        var plateId = result.Instances.Min(i => (long)i.Id) - 1;
        foreach (var instance in result.Instances)
        {
            var layer = result.Layers.GetValueOrDefault(instance.Id, "Runtime");
            if (instance is ParsedTerrainInstance terrain)
            {
                var tera = terrain.Data?.TeraFile ?? throw new InvalidDataException("Terrain data is not loaded.");
                for (var index = 0; index < tera.Header.PlateCount; index++)
                {
                    var pos = tera.GetPlatePosition(index);
                    var path = $"{terrain.Path.GamePath}/bgplate/{index:D4}.mdl";
                    var matrix = Matrix4x4.CreateTranslation(pos.X, 0, pos.Y) * terrain.Transform.AffineTransform.Matrix;
                    var plate = new ParsedBgPartsInstance((nint)plateId--, true, new Transform(matrix), path, null);
                    entries.Add(new(plate, path, "", -1, 0, layer));
                }
            }
            else if (instance is ParsedBgPartsInstance bg)
                entries.Add(new(bg, bg.Path.FullPath, bg.BgChangeMaterial?.MaterialPath ?? "",
                    bg.BgChangeMaterial?.BGChangeMaterialIndex ?? -1, bg.Stain?.RowId ?? 0, layer, bg.BgStainLinear, bg.LayoutColor, bg.BgEmissivePower));
        }
        return TerritoryGrouping.Build(entries, mode);
    }

    public Territory[] GetTerritories()
    {
        var places = excel.GetExcelSheet<PlaceName>();
        return excel.GetExcelSheet<TerritoryType>()
            .Where(t => !string.IsNullOrWhiteSpace(t.Bg.ToString()))
            .Select(t => new Territory(t.RowId, $"{places.GetRowOrDefault(t.PlaceName.RowId)?.Name.ExtractText()} ({t.Name}) [{t.RowId}]", t.Bg.ToString()))
            .OrderBy(t => t.Name).ToArray();
    }

    public string FestivalName(TerritorySceneFile.Festival festival)
    {
        var row = excel.GetExcelSheet<Festival>().GetRowOrDefault(festival.Id);
        var name = row?.Name.ExtractText();
        return $"{(string.IsNullOrEmpty(name) ? "节庆 " + festival.Id : name)} / 阶段 {festival.Phase} [{festival.Id}]";
    }

    private byte[]? Read(string path) => pack.GetFile(path)?.File.RawData.ToArray();

    public Document Load(Territory territory, CancellationToken token)
    {
        var path = territory.Bg.Replace('\\', '/').TrimStart('/');
        if (!path.StartsWith("bg/", StringComparison.OrdinalIgnoreCase)) path = "bg/" + path;
        if (!path.EndsWith(".lvb", StringComparison.OrdinalIgnoreCase)) path += ".lvb";
        var scene = new TerritorySceneFile(Read(path) ?? throw new FileNotFoundException("找不到区域场景", path));
        var layers = new List<SourceLayer>();
        layers.AddRange(scene.Layers.Select(l => new SourceLayer(path, l)));
        foreach (var resource in scene.LayerResources.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            token.ThrowIfCancellationRequested();
            var lgb = new TerritorySceneFile(Read(resource) ?? throw new FileNotFoundException("找不到区域布局", resource));
            layers.AddRange(lgb.Layers.Select(l => new SourceLayer(resource, l)));
        }
        return new(territory, path, scene.TerrainPath.TrimEnd('/'), layers.ToArray(), scene.Filters.ToArray());
    }

    public Result Build(Document document, Selection selection, TerritorySnapshot? runtime, CancellationToken token)
    {
        if (runtime?.TerritoryId != document.Territory.Id) runtime = null;
        if (selection.UseCurrentState && runtime == null)
            throw new InvalidOperationException("该区域已不再加载，请重新选择静态分支或返回该区域。");
        var filterKey = selection.UseCurrentState && runtime != null
            ? document.Filters.FirstOrDefault(f => f.TerritoryId == runtime.TerritoryId && f.ContentId == runtime.ContentId)?.Key
                ?? runtime.FilterKey
            : selection.FilterKey;
        var festivals = selection.UseCurrentState && runtime != null ? runtime.Festivals : selection.Festivals;
        // Runtime state from a different scene version must not alter a manually selected version.
        var runtimeKey = runtime == null ? (uint?)null : document.Filters
            .FirstOrDefault(f => f.TerritoryId == runtime.TerritoryId && f.ContentId == runtime.ContentId)?.Key ?? runtime.FilterKey;
        if (runtimeKey != filterKey) runtime = null;
        var report = new Report { TerritoryId = document.Territory.Id, FilterKey = filterKey,
            Festivals = festivals.ToArray(), ComparedWithRuntime = runtime != null };
        var output = new List<ParsedInstance>();
        var origins = new Dictionary<nint, string>();
        var origin = "Terrain";
        foreach (var source in document.Layers.Where(l => l.Layer.FilterOperation > 2))
            report.Warnings.Add($"跳过未知层过滤规则 {source.Layer.FilterOperation}: {source.Resource} / {source.Layer.Name}");
        long nextId = -1;
        nint Id() => (nint)nextId--;
        var cache = new Dictionary<string, TerritorySceneFile>(StringComparer.OrdinalIgnoreCase);
        var stack = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var modelExists = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var matchedRoots = new HashSet<TerritoryRuntimeNode>();
        var handledRuntime = new HashSet<TerritoryRuntimeNode>(ReferenceEqualityComparer.Instance);
        var allStaticRoots = document.Layers.SelectMany(l => l.Layer.Instances.Select(i =>
            (l.Layer.RuntimeKey, i.RuntimeType, i.Id))).ToHashSet();
        if (document.TerrainPath.Length != 0)
        {
            var tera = Read(document.TerrainPath + "/bgplate/terrain.tera");
            if (tera == null) report.MissingResources.Add(document.TerrainPath + "/bgplate/terrain.tera");
            else
            {
                var terrain = new ParsedTerrainInstance(Id(), new Transform(Vector3.Zero, Quaternion.Identity, Vector3.One),
                    document.TerrainPath, Vector3.Zero) { Data = new ParsedTerrainInstanceData(new TeraFile(tera)) };
                output.Add(terrain);
                origins[terrain.Id] = origin;
            }
        }
        foreach (var source in document.Layers)
        {
            var layer = source.Layer;
            origin = $"{source.Resource}__{layer.GroupId}_{layer.Id}_{layer.Name}";
            if (!layer.Matches(filterKey, festivals)) continue;
            // LayoutManager.Layers is keyed uniquely by LayerKey within this layout.
            var liveLayers = runtime?.Layers.Where(l => l.Key == layer.RuntimeKey && l.Festival == layer.Festival).ToArray() ?? [];
            foreach (var instance in layer.Instances)
            {
                var matches = liveLayers.SelectMany(l => l.Nodes).Where(n => n.Type == instance.RuntimeType && n.InstanceId == instance.Id).ToArray();
                var live = matches.Length == 1 ? matches[0] : null;
                if (live != null) matchedRoots.Add(live);
                Add(instance, Matrix4x4.Identity, live);
            }
        }
        if (runtime != null)
        foreach (var layer in runtime.Layers.Where(l => l.Festival.Id == 0 || festivals.Contains(l.Festival)))
        foreach (var node in layer.Nodes)
        {
            // Do not resurrect static roots excluded by the chosen layer filter.
            if (!matchedRoots.Contains(node) && !allStaticRoots.Contains((layer.Key, node.Type, node.InstanceId)))
            {
                origin = $"Runtime__{layer.GroupKey}_{layer.Key}";
                AddRuntime(node);
            }
        }
        if (runtime != null)
        foreach (var node in runtime.AdditionalHousing)
        {
            origin = "Runtime__Housing";
            AddRuntime(node);
        }
        if (report.UnmatchedRuntimeChildren != 0)
            report.Warnings.Add("部分 SGB 子物件无法按预制体索引和资源路径匹配，保留了对应静态布局，未叠加未匹配的运行时子物件。");
        if (runtime != null && report.MatchedRuntimeInstances == 0 && report.HousingModels == 0)
            report.Warnings.Add("未匹配到静态布局中的运行时实例；请核对该区域的布局标识。静态布局仍已导出。");
        if (output.Count == 0) throw new InvalidDataException("所选分支没有可导出的地形或模型。");
        return new(output.ToArray(), report, origins);

        void Add(TerritorySceneFile.Instance item, Matrix4x4 parent, TerritoryRuntimeNode? live,
            ParsedStain? inheritedStain = null)
        {
            token.ThrowIfCancellationRequested();
            if (live is { IsHousing: true, Children.Length: > 0 })
            {
                AddHousing(live);
                return;
            }
            if (live != null && !handledRuntime.Add(live)) return;
            if (live != null) report.MatchedRuntimeInstances++;
            if (live is { Active: false }) { report.InactiveInstances++; return; }
            var stain = live?.Stain ?? inheritedStain;
            var world = item.LocalMatrix * parent;
            if (item.Type == 1)
            {
                if (live != null)
                {
                    world = live.Transform.AffineTransform.Matrix;
                    if (live.Model != null)
                    {
                        AddModel(live.Model.Path.FullPath, world, live.Model, stain);
                        return;
                    }
                }
                AddModel(item.Path, world, null, stain);
                return;
            }
            if (item.Type is not (6 or 45))
            {
                report.UnsupportedTypes[item.Type] = report.UnsupportedTypes.GetValueOrDefault(item.Type) + 1;
                return;
            }
            var path = item.Path;
            if (item.Type == 45)
            {
                var row = excel.GetExcelSheet<EObj>().GetRowOrDefault(item.EventObjectId);
                var sharedId = row?.SgbPath.RowId ?? 0;
                path = sharedId == 0 ? "" : excel.GetExcelSheet<ExportedSG>().GetRowOrDefault(sharedId)?.SgbPath.ToString() ?? "";
                if (row == null || (sharedId != 0 && path.Length == 0))
                    report.Warnings.Add($"事件物件 {item.EventObjectId} 没有可解析的 ExportedSG 引用 ({sharedId})。");
            }
            // Read the replacement prefab too, so its unloaded children can still be exported.
            if (live != null && !string.IsNullOrEmpty(live.Path)) path = live.Path;
            if (string.IsNullOrEmpty(path)) return;
            if (!stack.Add(path)) { report.Warnings.Add("循环 SGB 引用: " + path); return; }
            try
            {
                if (!cache.TryGetValue(path, out var scene))
                {
                    var bytes = Read(path);
                    if (bytes == null) { report.MissingResources.Add(path); return; }
                    cache[path] = scene = new TerritorySceneFile(bytes);
                }
                if (live != null) world = live.Transform.AffineTransform.Matrix;
                var used = new HashSet<TerritoryRuntimeNode>();
                foreach (var layer in scene.Layers.Where(l => l.Matches(filterKey, festivals)))
                for (var index = 0; index < layer.Instances.Length; index++)
                {
                    var child = layer.Instances[index];
                    var candidates = live?.Children.Where(n => n.PrefabIndex == index && n.Type == child.Type
                        && string.Equals(n.Path, child.Path, StringComparison.OrdinalIgnoreCase)).ToArray() ?? [];
                    var childLive = candidates.Length == 1 ? candidates[0] : null;
                    if (childLive != null) used.Add(childLive);
                    Add(child, world, childLive, stain);
                }
                if (live != null) report.UnmatchedRuntimeChildren += live.Children.Count(n => !used.Contains(n));
            }
            finally { stack.Remove(path); }
        }

        void AddRuntime(TerritoryRuntimeNode node)
        {
            token.ThrowIfCancellationRequested();
            if (node is { IsHousing: true, Children.Length: > 0 })
            {
                AddHousing(node);
                return;
            }
            if (!node.Active) { report.InactiveInstances++; return; }
            if (node.Type == 1)
            {
                if (!handledRuntime.Add(node)) return;
                var path = node.Model?.Path.FullPath ?? node.Path;
                AddModel(path, node.Transform.AffineTransform.Matrix, node.Model, node.Stain);
            }
            else if (!string.IsNullOrEmpty(node.Path))
                Add(new TerritorySceneFile.Instance(6, node.InstanceId, node.Path, Vector3.Zero,
                    Vector3.Zero, Vector3.One, node.Path, 0), Matrix4x4.Identity, node);
        }

        void AddHousing(TerritoryRuntimeNode root)
        {
            if (handledRuntime.Contains(root)) return;
            report.HousingRoots++;
            Visit(root, root.Stain);
            void Visit(TerritoryRuntimeNode node, ParsedStain? inheritedStain)
            {
                token.ThrowIfCancellationRequested();
                if (!handledRuntime.Add(node)) return;
                if (!node.Active) { report.InactiveInstances++; return; }
                var stain = node.Stain ?? inheritedStain;
                if (node.Type == 1)
                {
                    var before = output.Count;
                    AddModel(node.Model?.Path.FullPath ?? node.Path,
                        node.Transform.AffineTransform.Matrix, node.Model, stain);
                    report.HousingModels += output.Count - before;
                    return;
                }
                // GetTransformImpl already includes all parent translation, rotation
                // and scale. Only file-owned SGB transforms need composition.
                foreach (var child in node.Children) Visit(child, stain);
                // Unloaded groups still need their resource data. Loaded children are
                // authoritative: never add the default prefab alongside replacements.
                if (node.Children.Length == 0 && node.Path.Length != 0)
                {
                    handledRuntime.Remove(node);
                    var before = output.Count;
                    AddRuntime(node);
                    report.HousingModels += output.Count - before;
                }
            }
        }

        void AddModel(string path, Matrix4x4 world, ParsedBgPartsInstance? live, ParsedStain? stain)
        {
            if (string.IsNullOrEmpty(path)) return;
            if (!modelExists.TryGetValue(path, out var exists))
                modelExists[path] = exists = Path.IsPathRooted(path) ? File.Exists(path) : pack.FileExists(path, out _);
            if (!exists) { report.MissingResources.Add(path); return; }
            // Graphics IsVisible includes camera culling; activation was checked separately above.
            var model = new ParsedBgPartsInstance(Id(), true, new Transform(world), path, live?.BgChangeMaterial)
                { Stain = stain, ModelPtr = live?.ModelPtr, BgStainLinear = live?.BgStainLinear, LayoutColor = live?.LayoutColor, BgEmissivePower = live?.BgEmissivePower };
            output.Add(model);
            origins[model.Id] = origin;
            if (live != null) report.RuntimeModels++; else report.StaticModels++;
        }
    }
}
