using System.Numerics;
using Meddle.Formats.Files;
using Meddle.Plugin.Models;
using Meddle.Plugin.Models.Layout;
using Meddle.Plugin.Models.Structs;
using Meddle.Plugin.Export.Ue;
using Meddle.Plugin.Services;
using Meddle.Utils.Export;
using Meddle.Utils.Export.Ue;
using Meddle.Plugin.Models.Composer;
using SharpGLTF.Scenes;
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
    var fileLight = new SceneLight(3, 2, 1, 0, 10, 30, "", 0xffffffff, 2, false, true, true, false, .1f, Vector2.Zero);
    var lamp = new TerritorySceneFile.Instance(3, 77, "Lamp", new(3, 4, 5), Vector3.Zero, Vector3.One, "", 0)
        { Light = fileLight };
    document = document with { Layers = [new("lights.lgb", new(0, 1, "Lights", default, 0, [], [lamp]))] };
    result = service.Build(document, new(0, [], false), null, default);
    var parsed = result.Instances.OfType<ParsedLightInstance>().Single();
    Check(parsed.Transform.Translation == lamp.Translation && parsed.Light.Range == fileLight.Range, "Static light data was lost.");
    Check(result.Report.StaticLights == 1 && result.Report.UnsupportedTypes.Count == 0, "Static light counted as unsupported.");
    foreach (var mode in Enum.GetValues<TerritoryGroupMode>())
        Check(service.Group(result, mode).SelectMany(g => g.Items).Single() == parsed, "Grouping dropped a light-only scene.");
    Check(UeInstanceExportService.ExportRoots(result.Instances).Length == 1, "UE selection dropped a light-only scene.");
    var liveLight = new TerritoryRuntimeNode(3, 77, 0, true, At(42), "", null, null, [])
        { Light = ParsedRenderLight.FromFile(fileLight with { HdrIntensity = 3 }) };
    runtime = new(1, 0, 0, [], [new(0, 1, default, [liveLight])]);
    result = service.Build(document, new(0, [], true), runtime, default);
    parsed = result.Instances.OfType<ParsedLightInstance>().Single();
    Check(parsed.Transform.Translation == At(42).Translation && parsed.Light.Color._vec3.X == 9, "Runtime light did not override its static match.");
    Check(parsed.Light.Source == fileLight && liveLight.Light!.Source!.HdrIntensity == 3,
        "Static provenance was lost or the runtime snapshot was mutated.");
    Check(result.Report.RuntimeLights == 1 && result.Report.StaticLights == 0, "Runtime light counted twice.");
    var lightHousing = root with { Children = [liveLight, liveLight with { InstanceId = 78, Active = false }] };
    document = document with { Layers = [] };
    runtime = runtime with { Layers = [], AdditionalHousing = [lightHousing] };
    result = service.Build(document, new(0, [], true), runtime, default);
    Check(result.Report.RuntimeLights == 1 && result.Report.HousingModels == 0 && result.Report.InactiveInstances == 1,
        "Housing lights lost activation or were counted as meshes.");

    // ComposeLight only uses its input and scene; avoid the unrelated model cache/game services.
    var composer = (InstanceComposer)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(InstanceComposer));
    var lightDocument = document with { Layers = [new("lights.lgb", new(0, 1, "Lights", default, 0, [], [lamp]))] };
    for (byte level = 0; level <= 5; level++)
    {
        var state = new HousingLightState(level, level / 5f);
        var dimmed = ParsedRenderLight.FromFile(fileLight, state.Multiplier, state);
        var controlled = liveLight with { Light = dimmed, HousingLightMultiplier = state.Multiplier };
        runtime = new(1, 0, 0, [], [new(0, 1, default, [controlled])]) { HousingLighting = state };
        var liveResult = service.Build(lightDocument, selection, runtime, default);
        var liveParsed = liveResult.Instances.OfType<ParsedLightInstance>().Single();
        Check(liveParsed.Light.Color.Intensity == state.Multiplier && liveParsed.Light.Range == dimmed.Range,
            "Housing brightness was multiplied into runtime light parameters twice.");
        Check(liveResult.Report.HousingLighting == state, "Report lost housing brightness.");

        var fallback = service.Build(lightDocument, selection, runtime with
        { Layers = [new(0, 1, default, [controlled with { Light = null }])] }, default);
        var fallbackParsed = fallback.Instances.OfType<ParsedLightInstance>().Single();
        Check(fallbackParsed.Light.Color.Intensity == state.Multiplier && fallbackParsed.Light.Range == dimmed.Range,
            "File fallback did not apply housing intensity and range.");
        Check(fallbackParsed.Light.HousingLighting == state && fallback.Report.StaticLights == 1,
            "File fallback lost housing provenance.");

        var unaffected = service.Build(lightDocument, selection, runtime with
        { Layers = [new(0, 1, default, [controlled with { Light = null, HousingLightMultiplier = null }])] }, default);
        Check(unaffected.Instances.OfType<ParsedLightInstance>().Single().Light.Color.Intensity == 1,
            "An ordinary lamp was dimmed merely because it was inside a house.");
        var otherBranch = service.Build(lightDocument, new(99, [], false), runtime, default);
        Check(otherBranch.Report.HousingLighting == null &&
            otherBranch.Instances.OfType<ParsedLightInstance>().Single().Light.Color.Intensity == 1,
            "Housing state leaked into a different scene branch.");

        var scene = new SceneBuilder();
        composer.ComposeLight(fallbackParsed, scene);
        var gltf = SharpGLTF.Schema2.ModelRoot.ParseGLB(scene.ToGltf2().WriteGLB().ToArray());
        var ue = UeLightExport.Read(gltf).Single();
        Check(MathF.Abs(ue.Intensity - 4 * state.Multiplier) < .0001f, "Exported light radiance is incorrect.");
        var extras = System.Text.Json.Nodes.JsonNode.Parse(ue.Metadata!)!;
        Check(extras["HousingLighting"]!["Level"]!.GetValue<byte>() == level,
            "Housing level was lost during GLB/UE conversion.");
    }
    Console.WriteLine("HOUSING_BRIGHTNESS_EXPORT_PROBE_PASSED");
    Console.WriteLine("TERRITORY_LIGHT_EXPORT_PROBE_PASSED");
    Console.WriteLine("TERRITORY_HOUSING_EXPORT_PROBE_PASSED");
}
finally { Directory.Delete(directory, true); }
