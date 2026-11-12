using System.Numerics;
using Meddle.Formats.Files;
using Meddle.Plugin.Models;
using Meddle.Plugin.Models.Layout;
using Meddle.Plugin.Services;
using Meddle.Utils.Export;
using static Meddle.Plugin.Services.LayoutService;

// Exercise the production builder without game memory or game resources.
var directory = Path.Combine(Path.GetTempPath(), "MeddleHousingProbe_" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(Path.Combine(directory, "game", "sqpack"));
try
{
    using var pack = new Meddle.SqPack.SqPack(directory);
    var service = new TerritoryExportService(null!, pack);
    var path = Path.Combine(directory, "placed.mdl");
    File.WriteAllBytes(path, []); // Build validates existence; composition is tested separately.
    Transform At(float x, float scale = 1) => new(new Vector3(x, 0, 0), Quaternion.Identity, new Vector3(scale));
    var appearance = new ParsedBgPartsInstance(1, false, At(2, 2), path, (0, "custom.mtrl"))
        { BgStainLinear = new(.2f, .3f, .4f, 1), LayoutColor = new(1, .5f, 1, 1), BgEmissivePower = .5f };
    var leaf = new TerritoryRuntimeNode(1, 1, 0, true, At(2, 2), path, null, appearance, []);
    var second = leaf with { InstanceId = 2, Transform = At(4, 2) };
    var hidden = leaf with { InstanceId = 3, Active = false };
    var nested = new TerritoryRuntimeNode(6, 4, 0, true, At(3, 2), "", null, null, [leaf, second, hidden]);
    var root = new TerritoryRuntimeNode(6, 5, 0, true, At(10, 2), "missing-default.sgb", null, null, [nested])
        { IsHousing = true };
    var document = new TerritoryExportService.Document(new(1, "Housing", "housing"), "housing.lvb", "", [], []);
    var selection = new TerritoryExportService.Selection(0, [], true);
    var runtime = new TerritorySnapshot(1, 0, 0, [], []) { AdditionalHousing = [root] };
    void Check(bool value, string message) { if (!value) throw new Exception(message); }
    var result = service.Build(document, selection, runtime, default);
    Check(result.Instances.Length == 2, "Runtime-only children were lost or hidden furniture was included.");
    var models = result.Instances.Cast<ParsedBgPartsInstance>().ToArray();
    Check(models.Select(m => m.Transform.Translation.X).SequenceEqual([2f, 4f]), "World placement was composed with its ancestors again.");
    Check(models.All(m => m.Transform.Scale == new Vector3(2)), "Runtime parent scale was applied twice.");
    Check(models.All(m => m.BgChangeMaterial == appearance.BgChangeMaterial && m.BgStainLinear == appearance.BgStainLinear
        && m.LayoutColor == appearance.LayoutColor && m.BgEmissivePower == appearance.BgEmissivePower), "Appearance was lost.");
    Check(result.Report.MissingResources.Count == 0, "Default prefab was read instead of captured furniture.");
    Check(result.Report.HousingModels == 2 && result.Report.InactiveInstances == 1, "Housing report is incorrect.");
    foreach (var mode in Enum.GetValues<TerritoryGroupMode>())
        Check(service.Group(result, mode).Sum(g => g.Items.Length) == 2, "Grouping lost an occurrence.");

    // The same snapshot node may be reachable from a layer and the housing list.
    runtime = runtime with { Layers = [new(0, 1, default, [root])] };
    Check(service.Build(document, selection, runtime, default).Instances.Length == 2, "Housing was exported twice.");
    var staticRoot = new TerritorySceneFile.Instance(6, 5, "House", Vector3.Zero, Vector3.Zero, Vector3.One, "default.sgb", 0);
    document = document with { Layers = [new("housing.lgb", new(0, 1, "Housing", default, 0, [], [staticRoot]))] };
    Check(service.Build(document, selection, runtime, default).Instances.Length == 2, "Matched housing did not use live children.");

    // GetTransformImpl returns world SRT, including non-commuting parent rotations
    // and scale. Neither nested housing nor matched runtime models may apply it twice.
    var world = new Transform(new Vector3(7, 11, -3), Quaternion.CreateFromYawPitchRoll(.7f, .2f, -.3f), new Vector3(2, 3, 4));
    var rotatedLeaf = leaf with { Transform = world };
    var rotatedRoot = root with { Transform = new Transform(new Vector3(20, 30, 40),
        Quaternion.CreateFromAxisAngle(Vector3.UnitY, 1.2f), new Vector3(5)), Children = [nested with { Children = [rotatedLeaf] }] };
    runtime = runtime with { Layers = [], AdditionalHousing = [rotatedRoot] };
    var placed = service.Build(document with { Layers = [] }, selection, runtime, default).Instances.Single();
    var point = new Vector3(1, 2, 3);
    Check(Vector3.Distance(Vector3.Transform(point, world.AffineTransform.Matrix),
        Vector3.Transform(point, placed.Transform.AffineTransform.Matrix)) < .0001f, "Housing world SRT changed.");
    var direct = new TerritorySceneFile.Instance(1, 1, "Direct", Vector3.Zero, Vector3.Zero, Vector3.One, path, 0);
    var directDocument = document with { Layers = [new("housing.lgb", new(0, 1, "Direct", default, 0, [], [direct]))] };
    runtime = runtime with { Layers = [new(0, 1, default, [rotatedLeaf])], AdditionalHousing = [] };
    placed = service.Build(directDocument, selection, runtime, default).Instances.Single();
    Check(Vector3.Distance(Vector3.Transform(point, world.AffineTransform.Matrix),
        Vector3.Transform(point, placed.Transform.AffineTransform.Matrix)) < .0001f, "Matched runtime world SRT changed.");

    // Keep a static model as a sentinel when runtime overrides are incompatible.
    runtime = runtime with { Layers = [], AdditionalHousing = [root] };
    var sentinel = new TerritorySceneFile.Instance(1, 9, "Static", Vector3.Zero, Vector3.Zero, Vector3.One, path, 0);
    document = document with { Layers = [new("housing.lgb", new(0, 1, "Static", default, 0, [], [sentinel]))] };
    var offline = service.Build(document, new(99, [], false), runtime, default);
    Check(offline.Instances.Length == 1 && offline.Report.HousingModels == 0, "Housing leaked into another branch.");
    offline = service.Build(document, new(0, [], false), runtime with { TerritoryId = 2 }, default);
    Check(offline.Instances.Length == 1 && offline.Report.HousingModels == 0, "Housing leaked into another territory.");
    Console.WriteLine("TERRITORY_HOUSING_EXPORT_PROBE_PASSED");
}
finally { Directory.Delete(directory, true); }
