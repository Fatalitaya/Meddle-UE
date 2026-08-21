using System.Numerics;
using System.Text.Json;
using Meddle.Utils.Export.Ue;
using Meddle.Utils.Files.Structs.Material;

namespace Meddle.Utils.Tests;

[TestClass]
public sealed class UeColorTableSerializationTests
{
    [TestMethod]
    public void Modern_MapsAllObservedRowSemantics()
    {
        var table = CreateModernColorTableSet();
        var snapshot = UeColorTableSnapshotMapper.Map(table) as UeModernColorTableSetSnapshot;

        Assert.IsNotNull(snapshot);
        Assert.AreEqual(32, snapshot!.ColorTable.Count);

        var row = snapshot.ColorTable[0];
        CollectionAssert.AreEqual(new[] { HalfRound(0.1f), HalfRound(0.2f), HalfRound(0.3f) }, row.Diffuse);
        CollectionAssert.AreEqual(new[] { HalfRound(0.4f), HalfRound(0.5f), HalfRound(0.6f) }, row.Specular);
        CollectionAssert.AreEqual(new[] { HalfRound(0.7f), HalfRound(0.8f), HalfRound(0.9f) }, row.Emissive);
        Assert.AreEqual(1.5f, row.GlossStrength);
        Assert.AreEqual(2.5f, row.SpecularStrength);
        Assert.AreEqual(BitConverter.HalfToUInt16Bits((Half)10), row.ShaderId);
        Assert.AreEqual((byte)64, row.TileIndex);
        Assert.AreEqual(0.75f, row.TileAlpha);
        Assert.AreEqual(BitConverter.HalfToUInt16Bits((Half)20), row.SphereIndex);
        Assert.AreEqual(HalfRound(150f), row.TileMatrix.UU);
        Assert.AreEqual(HalfRound(0f), row.TileMatrix.UV);
        Assert.AreEqual(HalfRound(0f), row.TileMatrix.VU);
        Assert.AreEqual(HalfRound(225f), row.TileMatrix.VV);
    }

    [TestMethod]
    public void Modern_DyeRowsPreserveFlagsAndChannel()
    {
        var table = CreateModernColorTableSet();
        var snapshot = UeColorTableSnapshotMapper.Map(table) as UeModernColorTableSetSnapshot;

        Assert.IsNotNull(snapshot);
        var dye = snapshot!.ColorDyeTable![0];
        Assert.AreEqual((ushort)123, dye.Template);
        Assert.AreEqual((byte)2, dye.Channel);
        Assert.IsTrue(dye.Diffuse);
        Assert.IsTrue(dye.Specular);
        Assert.IsTrue(dye.Emissive);
        Assert.IsTrue(dye.Scalar3);
        Assert.IsTrue(dye.Metalness);
        Assert.IsTrue(dye.Roughness);
        Assert.IsTrue(dye.SheenRate);
        Assert.IsTrue(dye.SheenTintRate);
        Assert.IsTrue(dye.SheenAperture);
        Assert.IsTrue(dye.Anisotropy);
        Assert.IsTrue(dye.SphereMapIndex);
        Assert.IsTrue(dye.SphereMapMask);
    }

    [TestMethod]
    public void Legacy_MapsExplicitFieldsAndPreservesRowOrder()
    {
        var table = CreateLegacyColorTableSet();
        var snapshot = UeColorTableSnapshotMapper.Map(table) as UeLegacyColorTableSetSnapshot;

        Assert.IsNotNull(snapshot);
        Assert.AreEqual(16, snapshot!.ColorTable.Count);

        var row = snapshot.ColorTable[0];
        CollectionAssert.AreEqual(new[] { HalfRound(0.01f), HalfRound(0.02f), HalfRound(0.03f) }, row.Diffuse);
        CollectionAssert.AreEqual(new[] { HalfRound(0.04f), HalfRound(0.05f), HalfRound(0.06f) }, row.Specular);
        CollectionAssert.AreEqual(new[] { HalfRound(0.07f), HalfRound(0.08f), HalfRound(0.09f) }, row.Emissive);
        Assert.AreEqual(0.25f, row.SpecularStrength);
        Assert.AreEqual(0.5f, row.GlossStrength);
        CollectionAssert.AreEqual(new[] { 1.25f, 2.25f }, row.MaterialRepeat);
        CollectionAssert.AreEqual(new[] { -0.5f, 0.75f }, row.MaterialSkew);
        Assert.AreEqual(BitConverter.HalfToUInt16Bits((Half)7), row.TileIndex);
    }

    [TestMethod]
    public void Legacy_DyeRowsPreserveTemplateAndFlags()
    {
        var table = CreateLegacyColorTableSet();
        var snapshot = UeColorTableSnapshotMapper.Map(table) as UeLegacyColorTableSetSnapshot;

        Assert.IsNotNull(snapshot);
        var dye = snapshot!.ColorDyeTable![0];
        Assert.AreEqual((ushort)321, dye.Template);
        Assert.IsTrue(dye.Diffuse);
        Assert.IsTrue(dye.Specular);
        Assert.IsTrue(dye.Emissive);
        Assert.IsTrue(dye.Gloss);
        Assert.IsTrue(dye.SpecularStrength);
    }

    [TestMethod]
    public void SerializedColorTable_HasNoEmptyVectorOrMatrixObjects()
    {
        var snapshot = new UeCharacterSnapshot
        {
            Root = new UeCharacterNodeSnapshot
            {
                NodeId = "root",
                Models =
                [
                    new UeModelSnapshot
                    {
                        Id = "root/model-0",
                        Materials =
                        [
                            new UeMaterialSnapshot
                            {
                                ColorTable = UeColorTableSnapshotMapper.Map(CreateModernColorTableSet())
                            }
                        ]
                    }
                ]
            }
        };

        using var document = JsonDocument.Parse(UeExportJson.Serialize(snapshot));
        var colorTable = document.RootElement
                                 .GetProperty("root")
                                 .GetProperty("models")[0]
                                 .GetProperty("materials")[0]
                                 .GetProperty("colorTable");

        Assert.AreEqual("Modern", colorTable.GetProperty("kind").GetString());
        AssertNoEmptyObjects(colorTable);

        var row = colorTable.GetProperty("colorTable")[0];
        Assert.AreEqual(3, row.GetProperty("diffuse").GetArrayLength());
        Assert.AreEqual(3, row.GetProperty("specular").GetArrayLength());
        Assert.AreEqual(3, row.GetProperty("emissive").GetArrayLength());
        Assert.AreEqual(HalfRound(150f), (float)row.GetProperty("tileMatrix").GetProperty("uu").GetDouble());
        Assert.AreEqual(HalfRound(0f), (float)row.GetProperty("tileMatrix").GetProperty("uv").GetDouble());
        Assert.AreEqual(HalfRound(0f), (float)row.GetProperty("tileMatrix").GetProperty("vu").GetDouble());
        Assert.AreEqual(HalfRound(225f), (float)row.GetProperty("tileMatrix").GetProperty("vv").GetDouble());
    }


    //[Fact]
    public void ModernColorTable_ShouldNeverSerializeEmptyVectorObjects()
    {
        var snapshot =
            JsonSerializer.Serialize(
                CreateModernColorTableSet());

        Assert.DoesNotContain(
            "\"diffuse\":{}",
            snapshot,
            StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain(
            "\"specular\":{}",
            snapshot,
            StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain(
            "\"tileMatrix\":{}",
            snapshot,
            StringComparison.OrdinalIgnoreCase);
    }
    private static ColorTableSet CreateModernColorTableSet()
    {
        var tableBytes = new byte[ColorTable.Size];
        using (var writer = new BinaryWriter(new MemoryStream(tableBytes)))
        {
            for (var rowIndex = 0; rowIndex < ColorTable.NumRows; rowIndex++)
            {
                WriteVec4(writer, 0.1f, 0.2f, 0.3f, 1.5f);
                WriteVec4(writer, 0.4f, 0.5f, 0.6f, 2.5f);
                WriteVec4(writer, 0.7f, 0.8f, 0.9f, 3.5f);
                WriteVec4(writer, 1.0f, 1.1f, 1.2f, 1.3f);
                WriteVec4(writer, 1.4f, 1.5f, 1.6f, 1.7f);
                WriteVec4(writer, 1.8f, 1.9f, 2.0f, 2.1f);
                WriteVec4(writer, 10f, 1f, 0.75f, 20f);
                WriteVec4(writer, 150f, 0f, 0f, 225f);
            }
        }

        var tableReader = new SpanBinaryReader(tableBytes);
        var colorTable = new ColorTable(ref tableReader);

        var dyeBytes = new byte[ColorTable.NumRows * sizeof(uint)];
        using (var writer = new BinaryWriter(new MemoryStream(dyeBytes)))
        {
            for (var rowIndex = 0; rowIndex < ColorTable.NumRows; rowIndex++)
            {
                writer.Write(CreateModernDyeRow().Data);
            }
        }

        var dyeReader = new SpanBinaryReader(dyeBytes);
        return new ColorTableSet
        {
            ColorTable = colorTable,
            ColorDyeTable = new ColorDyeTable(ref dyeReader)
        };
    }

    private static LegacyColorTableSet CreateLegacyColorTableSet()
    {
        var tableBytes = new byte[LegacyColorTable.Size];
        using (var writer = new BinaryWriter(new MemoryStream(tableBytes)))
        {
            for (var rowIndex = 0; rowIndex < LegacyColorTable.LegacyNumRows; rowIndex++)
            {
                WriteVec4(writer, 0.01f, 0.02f, 0.03f, 0.25f);
                WriteVec4(writer, 0.04f, 0.05f, 0.06f, 0.5f);
                WriteVec4(writer, 0.07f, 0.08f, 0.09f, 7f);
                WriteVec4(writer, 1.25f, 2.25f, -0.5f, 0.75f);
            }
        }

        var tableReader = new SpanBinaryReader(tableBytes);
        var colorTable = new LegacyColorTable(ref tableReader);

        var dyeBytes = new byte[LegacyColorTable.LegacyNumRows * sizeof(ushort)];
        using (var writer = new BinaryWriter(new MemoryStream(dyeBytes)))
        {
            for (var rowIndex = 0; rowIndex < LegacyColorTable.LegacyNumRows; rowIndex++)
            {
                writer.Write(CreateLegacyDyeRow().Data);
            }
        }

        var dyeReader = new SpanBinaryReader(dyeBytes);
        return new LegacyColorTableSet
        {
            ColorTable = colorTable,
            ColorDyeTable = new LegacyColorDyeTable(ref dyeReader)
        };
    }

    private static ColorDyeTableRow CreateModernDyeRow()
    {
        var row = new ColorDyeTableRow
        {
            Template = 123,
            Channel = 2,
            DiffuseColor = true,
            SpecularColor = true,
            EmissiveColor = true,
            Scalar3 = true,
            Metalness = true,
            Roughness = true,
            SheenRate = true,
            SheenTintRate = true,
            SheenAperture = true,
            Anisotropy = true,
            SphereMapIndex = true,
            SphereMapMask = true
        };
        return row;
    }

    private static LegacyColorDyeTableRow CreateLegacyDyeRow()
    {
        var row = new LegacyColorDyeTableRow
        {
            Template = 321,
            DiffuseColor = true,
            SpecularColor = true,
            EmissiveColor = true,
            Shininess = true,
            SpecularMask = true
        };
        return row;
    }

    private static void WriteVec4(BinaryWriter writer, float x, float y, float z, float w)
    {
        writer.Write(BitConverter.HalfToUInt16Bits((Half)x));
        writer.Write(BitConverter.HalfToUInt16Bits((Half)y));
        writer.Write(BitConverter.HalfToUInt16Bits((Half)z));
        writer.Write(BitConverter.HalfToUInt16Bits((Half)w));
    }

    private static float HalfRound(float value)
    {
        return (float)BitConverter.UInt16BitsToHalf(BitConverter.HalfToUInt16Bits((Half)value));
    }

    private static void AssertNoEmptyObjects(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            Assert.IsTrue(element.EnumerateObject().Any(), "Suspicious empty object in ColorTable subtree.");
            foreach (var property in element.EnumerateObject())
            {
                AssertNoEmptyObjects(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                AssertNoEmptyObjects(item);
            }
        }
    }
}
