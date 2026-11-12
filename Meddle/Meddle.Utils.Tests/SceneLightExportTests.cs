using System.Numerics;
using System.Text.Json.Nodes;
using Meddle.Utils.Export;
using Meddle.Utils.Export.Ue;
using Meddle.Unreal;
using SharpGLTF.Schema2;
using SharpGLTF.Scenes;

namespace Meddle.Utils.Tests;

[TestClass]
public sealed class SceneLightExportTests
{
    [TestMethod]
    public void HousingBrightness_RecalculatesRangeForEachFalloffAndKeepsDirectionalRange()
    {
        var light = new Meddle.Formats.Files.SceneLight(2, 1, 1, 0, 0, 0, "", 0xffffffff,
            1, false, false, false, false, .1f, Vector2.Zero);
        for (var level = 0; level <= 5; level++)
        {
            var multiplier = level / 5f;
            var value = 51 * multiplier;
            Assert.AreEqual(value, light.RangeWithMultiplier(multiplier), .0001f);
            Assert.AreEqual(MathF.Sqrt(value), (light with { Attenuation = 2 }).RangeWithMultiplier(multiplier), .0001f);
            Assert.AreEqual(MathF.Pow(value, 1f / 3), (light with { Attenuation = 3 }).RangeWithMultiplier(multiplier), .0001f);
            Assert.AreEqual(10000f, (light with { Type = 1 }).RangeWithMultiplier(multiplier));
        }
    }

    [TestMethod]
    public void Spot_RoundtripPreservesLinearRadianceWorldDirectionAndRange()
    {
        var scene = new SceneBuilder();
        var world = Matrix4x4.CreateScale(3) * Matrix4x4.CreateRotationY(.7f) * Matrix4x4.CreateTranslation(2, 4, 6);
        SceneLightExport.Add(scene, "Spot", world, 3, new(4, 2, 1), .5f, 12, 30, 10,
            new JsonObject { ["ObjectShadow"] = true, ["Reflection"] = false });
        var gltf = ModelRoot.ParseGLB(scene.ToGltf2().WriteGLB().ToArray());
        var node = gltf.LogicalNodes.Single(n => n.PunctualLight != null);
        var light = node.PunctualLight!;
        Assert.IsTrue(Vector3.Distance(light.Color * light.Intensity, new(2, 1, .5f)) < .0001f);
        Assert.AreEqual(12f, light.Range);
        var expectedDirection = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitZ, world));
        Assert.IsTrue(Vector3.Distance(expectedDirection, Vector3.Normalize(Vector3.TransformNormal(-Vector3.UnitZ, node.WorldMatrix))) < .0001f);
        var ue = UeLightExport.Read(gltf).Single();
        Assert.AreEqual(new Vector3(-600, 200, 400), ue.Position);
        Assert.IsTrue(Vector3.Distance(ue.Direction, Coordinates.Direction(expectedDirection)) < .0001f);
        Assert.AreEqual(1200f, ue.Range);
        Assert.AreEqual(30f, ue.InnerCone, .001f);
        Assert.AreEqual(40f, ue.OuterCone, .001f);
        Assert.IsTrue(ue.CastShadows);
        Assert.IsFalse(ue.Specular);
    }

    [TestMethod]
    public void ZeroRangeDirectionalAndBlackPointRemainValidAndExportable()
    {
        var scene = new SceneBuilder();
        SceneLightExport.Add(scene, "Sun", Matrix4x4.Identity, 1, Vector3.One, 2, 0, 0, 0);
        SceneLightExport.Add(scene, "Off", Matrix4x4.Identity, 2, Vector3.Zero, 1, 0, 0, 0);
        var gltf = ModelRoot.ParseGLB(scene.ToGltf2().WriteGLB().ToArray());
        Assert.AreEqual(2, gltf.LogicalPunctualLights.Count);
        Assert.AreEqual(0f, gltf.LogicalPunctualLights.Single(l => l.LightType == PunctualLightType.Point).Intensity);
        var lights = UeLightExport.Read(gltf);
        Assert.AreEqual(LevelLightType.Directional, lights.Single(l => l.Name == "Sun").Type);
    }

    [TestMethod]
    public void NativeLightOnlyExport_WritesLoadableMapAndFiniteMetadata()
    {
        var directory = Path.Combine(Path.GetTempPath(), "MeddleLightTests", Guid.NewGuid().ToString("N"));
        try
        {
            var scene = new SceneBuilder();
            SceneLightExport.Add(scene, "Sun", Matrix4x4.Identity, 1, Vector3.One, 2, 0, 0, 0);
            NativeAssetExporter.Export(scene.ToGltf2(), directory, "Lights");
            var path = Path.Combine(directory, "Content/Meddle/Lights/Lights_Lights.umap");
            var map = new UAssetAPI.UAsset(path, UAssetAPI.UnrealTypes.EngineVersion.VER_UE5_8);
            Assert.IsTrue(map.Imports.Any(i => i.ObjectName.ToString() == "DirectionalLightComponent"));
            var json = JsonNode.Parse(File.ReadAllText(Path.ChangeExtension(path, ".lights.json")))!;
            Assert.AreEqual(0f, json[0]!["Range"]!.GetValue<float>());
            Assert.AreEqual(2f, json[0]!["Intensity"]!.GetValue<float>());
        }
        finally
        {
            var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "MeddleLightTests")) + Path.DirectorySeparatorChar;
            if (Path.GetFullPath(directory).StartsWith(root, StringComparison.OrdinalIgnoreCase) && Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }
}
