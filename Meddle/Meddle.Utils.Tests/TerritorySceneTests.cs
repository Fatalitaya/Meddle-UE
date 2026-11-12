using System.Numerics;
using System.Text;
using Meddle.Formats.Files;

namespace Meddle.Utils.Tests;

[TestClass]
public sealed class TerritorySceneTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void LightPayload_ReadsLegacyAndV700WithoutConfusingTheMergeGroup(bool v7)
    {
        var bytes = MakeLayerFile();
        using var writer = new BinaryWriter(new MemoryStream(bytes));
        void Int(int offset, uint value) { writer.BaseStream.Position = 192 + offset; writer.Write(value); }
        void Float(int offset, float value) { writer.BaseStream.Position = 192 + offset; writer.Write(value); }
        Int(0, 3); Int(0x30, 3); Float(0x34, 2); Float(0x38, 4); Int(0x3c, 1);
        Float(0x40, 10); Float(0x44, 30); Int(0x48, 208); Int(0x4c, 0xff4080ff); Float(0x50, 2);
        bytes[192 + 0x54] = 1; bytes[192 + 0x58] = 1; bytes[192 + 0x59] = 1;
        Float(0x5c, .5f); Float(0x60, 12); Float(0x64, 34); Int(0x68, 7);
        if (v7) { Float(0x68, .75f); Float(0x80, 8); Int(0x88, 23u << 16 | 1u); Int(0x8c, 0x56373030); }
        writer.BaseStream.Position = 400; writer.Write(Encoding.UTF8.GetBytes("bg/light.tex\0"));
        var light = new TerritorySceneFile(bytes).Layers.Single().Instances.Single().Light!;
        Assert.AreEqual(3u, light.Type);
        Assert.AreEqual("bg/light.tex", light.TexturePath);
        Assert.AreEqual(new Vector2(12, 34), light.PlaneAngles);
        Assert.AreEqual(30f, light.ConeAngle);
        Assert.AreEqual(10f, light.FalloffAngle);
        Assert.AreEqual(4f, light.LinearColor.X, .0001f);
        Assert.AreEqual(1u, light.FalloffType);
        Assert.AreEqual(MathF.Sqrt(51), light.Range, .0001f);
        Assert.IsTrue(light.SpecularEnabled && light.ObjectShadowEnabled && !light.CharacterShadowEnabled);
        if (v7)
        {
            Assert.AreEqual(.75f, light.Dawntrail!.LightSelect);
            Assert.AreEqual((ushort)23, light.Dawntrail.MergeGroupId);
            Assert.AreEqual(8f, light.Dawntrail.Fade);
        }
        else { Assert.IsNull(light.Dawntrail); Assert.AreEqual((ushort)7, light.MergeGroupId); }
    }

    [TestMethod]
    public void Lgb_ResolvesEachOffsetFromItsOwnStructure()
    {
        var file = new TerritorySceneFile(MakeLayerFile());
        var layer = file.Layers.Single();
        Assert.AreEqual(70000u, layer.Id);
        Assert.AreEqual(unchecked((ushort)70000), layer.RuntimeKey);
        Assert.AreEqual("festival", layer.Name);
        Assert.AreEqual(new TerritorySceneFile.Festival(17, 2), layer.Festival);
        CollectionAssert.AreEqual(new uint[] { 67908 }, layer.FilterKeys);
        var instance = layer.Instances.Single();
        Assert.AreEqual(123u, instance.Id);
        Assert.AreEqual("bg/test.mdl", instance.Path);
        Assert.AreEqual(new Vector3(10, 20, 30), instance.Translation);
    }

    [TestMethod]
    public void FestivalChoice_KeepsCommonLayersAndOnlyTheSelectedPhase()
    {
        var selected = new HashSet<TerritorySceneFile.Festival> { new(17, 2), new(25, 1) };
        var layer = new TerritorySceneFile(MakeLayerFile()).Layers.Single();
        Assert.IsTrue(layer.Matches(67908, selected));
        Assert.IsFalse(layer.Matches(67908, new HashSet<TerritorySceneFile.Festival> { new(17, 1) }));
        Assert.IsFalse(layer.Matches(67908, new HashSet<TerritorySceneFile.Festival>()));
        Assert.IsFalse(layer.Matches(999, selected));
        Assert.IsTrue((layer with { Festival = default }).Matches(67908, new HashSet<TerritorySceneFile.Festival>()));
        Assert.IsTrue((layer with { Festival = new(25, 1) }).Matches(67908, selected));
    }

    [TestMethod]
    public void SceneFilter_ExclusionDoesNotExportMutuallyExclusiveLayers()
    {
        var layer = new TerritorySceneFile(MakeLayerFile()).Layers.Single() with { Festival = default, FilterOperation = 2 };
        var festivals = new HashSet<TerritorySceneFile.Festival>();
        Assert.IsFalse(layer.Matches(67908, festivals));
        Assert.IsTrue(layer.Matches(999, festivals));
        Assert.IsTrue((layer with { FilterOperation = 0 }).Matches(67908, festivals));
        Assert.IsFalse((layer with { FilterOperation = 3 }).Matches(67908, festivals));
    }

    [TestMethod]
    public void Lvb_ReadsSceneFiltersResourcesAndTerrain()
    {
        var bytes = new byte[512];
        using var writer = new BinaryWriter(new MemoryStream(bytes));
        void Int(int offset, int value) { writer.BaseStream.Position = offset; writer.Write(value); }
        void Str(int offset, string value) { writer.BaseStream.Position = offset; writer.Write(Encoding.UTF8.GetBytes(value + '\0')); }
        Int(0, 0x3142564c); Int(4, bytes.Length); Int(8, 1);
        Int(12, 0x314e4353); Int(16, bytes.Length - 12);
        // SCN data starts at 20; offsets below are relative to it.
        Int(28, 64); Int(32, 160); Int(40, 240); Int(44, 1);
        Int(88, 96); Str(180, "bg/test");
        // Move the filter list away from the terrain string.
        Int(32, 192); Int(212, 12); Int(216, 1);
        Int(228, 67908);
        writer.BaseStream.Position = 240; writer.Write((ushort)148); writer.Write((ushort)23);
        Int(260, 12); Str(272, "bg/test/level/bg.lgb");
        var scene = new TerritorySceneFile(bytes);
        Assert.AreEqual("bg/test", scene.TerrainPath);
        Assert.AreEqual(new TerritorySceneFile.Filter(67908, 148, 23), scene.Filters.Single());
        Assert.AreEqual("bg/test/level/bg.lgb", scene.LayerResources.Single());
    }

    [TestMethod]
    public void Sgb_ReadsEmbeddedLayerGroup()
    {
        var lgb = MakeLayerFile();
        var sgb = new byte[lgb.Length + 64];
        using var writer = new BinaryWriter(new MemoryStream(sgb));
        writer.Write(0x31424753); writer.Write(sgb.Length); writer.Write(1);
        writer.Write(0x314e4353); writer.Write(sgb.Length - 12);
        writer.Write(64); writer.Write(1);
        Array.Copy(lgb, 20, sgb, 84, lgb.Length - 20);
        var scene = new TerritorySceneFile(sgb);
        Assert.AreEqual("bg/test.mdl", scene.Layers.Single().Instances.Single().Path);
    }

    [TestMethod]
    public void NestedPrefab_UsesLocalScaleThenRotationThenParentTransform()
    {
        var child = new TerritorySceneFile.Instance(1, 1, "child", new(3, 0, 0), Vector3.Zero, Vector3.One, "", 0);
        var parent = child with { Translation = new(10, 0, 0), Rotation = new(0, MathF.PI / 2, 0), Scale = new(2, 2, 2) };
        var point = Vector3.Transform(Vector3.UnitX, child.LocalMatrix * parent.LocalMatrix);
        Assert.AreEqual(10f, point.X, 0.0001f);
        Assert.AreEqual(-8f, point.Z, 0.0001f);
    }

    [TestMethod]
    public void InvalidOffset_ReportsBadSceneInsteadOfReadingOutsideBuffer()
    {
        var bytes = MakeLayerFile();
        BitConverter.GetBytes(bytes.Length + 16).CopyTo(bytes, 40);
        Assert.ThrowsExactly<InvalidDataException>(() => new TerritorySceneFile(bytes));
    }

    private static byte[] MakeLayerFile()
    {
        var bytes = new byte[512];
        using var writer = new BinaryWriter(new MemoryStream(bytes));
        void Int(int offset, int value) { writer.BaseStream.Position = offset; writer.Write(value); }
        void Str(int offset, string value) { writer.BaseStream.Position = offset; writer.Write(Encoding.UTF8.GetBytes(value + '\0')); }
        Int(0, 0x3142474c); Int(4, bytes.Length); Int(8, 1);
        Int(12, 0x3150474c); Int(16, bytes.Length - 12);
        Int(20, 7); Int(28, 20); Int(32, 1); Int(40, 24);
        // Layer at 64, reference list at 128, its key array at 144.
        Int(64, 70000); Int(68, 300); Int(72, 96); Int(76, 1); Int(84, 64);
        writer.BaseStream.Position = 88; writer.Write((ushort)17); writer.Write((ushort)2);
        Int(128, 1); Int(132, 16); Int(136, 1); Int(144, 67908);
        // Instance offsets at 160, instance at 192, path at 320.
        Int(160, 32); Int(192, 1); Int(196, 123); Int(200, 160);
        writer.BaseStream.Position = 204;
        foreach (var f in new[] { 10f, 20f, 30f, 0f, 0f, 0f, 1f, 1f, 1f }) writer.Write(f);
        Int(240, 128);
        Str(320, "bg/test.mdl"); Str(352, "model"); Str(364, "festival");
        return bytes;
    }
}
