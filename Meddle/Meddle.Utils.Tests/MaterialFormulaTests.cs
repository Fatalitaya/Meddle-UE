using System.Runtime.InteropServices;
using System.Buffers.Binary;
using System.Text.Json.Nodes;
using Meddle.Formats;
using Meddle.Formats.Files.MtrlFile;
using Meddle.Utils.Export.Ue;
using Meddle.Utils.Helpers;
using Meddle.Unreal;

namespace Meddle.Utils.Tests;

[TestClass]
public sealed class MaterialFormulaTests
{
    [TestMethod]
    public void Equipment_OnlyCompatibilityRequiresDiffuse()
    {
        var rows=new JsonArray();
        for(var i=0;i<32;i++) rows.Add(JsonNode.Parse("""{"Diffuse":{"X":1,"Y":1,"Z":1},"Emissive":{"X":0,"Y":0,"Z":0},"Metalness":0,"Roughness":0.5,"TileIndex":0,"TileAlpha":0,"TileMatrix":{"UU":1,"VV":1}}"""));
        var data=new JsonObject { ["ShaderPackage"]="character.shpk",
            ["ColorTable"]=new JsonObject { ["ColorTable"]=new JsonObject { ["Rows"]=rows } } };
        var textures=new[]{"g_SamplerNormal","g_SamplerMask","g_SamplerIndex","chara_tile_norm_array","chara_tile_orb_array"}
            .ToDictionary(k=>k,k=>new MaterialTexture(k,"/Game/Test/"+k,false));
        Assert.IsNotNull(CharacterMaterial.Build(data,textures).Graph);
        data["GetValues"]="GetValuesCompatibility";
        var missing=CharacterMaterial.Build(data,textures);
        Assert.IsNull(missing.Graph);
        StringAssert.Contains(missing.Gap!,"g_SamplerDiffuse");
        textures["g_SamplerDiffuse"]=new("g_SamplerDiffuse","/Game/Test/D",true);
        Assert.IsNotNull(CharacterMaterial.Build(data,textures).Graph);
    }

    [TestMethod]
    public void FaceOverlays_UseOnlyNormalAndRequireDecalOnlyWhenEnabled()
    {
        var textures=new Dictionary<string,MaterialTexture> { ["g_SamplerNormal"]=new("g_SamplerNormal","/Game/Test/N",false) };
        var data=new JsonObject { ["ShaderPackage"]="charactertattoo.shpk" };
        var tattoo=CharacterMaterial.Build(data,textures);
        Assert.IsNotNull(tattoo.Graph);
        Assert.AreEqual("BLEND",tattoo.AlphaMode);
        data["GetDecalColor"]="GetDecalColorAlpha";
        Assert.IsNull(CharacterMaterial.Build(data,textures).Graph);
        textures["Decal"]=new("Decal","/Game/Test/D",false);
        Assert.IsNotNull(CharacterMaterial.Build(data,textures).Graph);
        data["ShaderPackage"]="characterocclusion.shpk";
        var occlusion=CharacterMaterial.Build(data,textures);
        Assert.IsNotNull(occlusion.Graph);
        Assert.IsTrue(occlusion.Unlit);
        Assert.AreEqual("MODULATE",occlusion.AlphaMode);
        Assert.IsFalse(string.IsNullOrEmpty(occlusion.Adaptation));
    }

    [TestMethod]
    public void ShpkVertexColor_PreservesAlphaAndSquaresRgbBeforeInterpolation()
    {
        var a=ShpkMaterial.VertexColor(new(0,0.5f,1,0.3f));
        Assert.AreEqual(new System.Numerics.Vector4(0,.25f,1,.3f),a);
        // A black/white edge interpolates to 0.5 after the VS, not 0.25.
        Assert.AreEqual(.5f,(ShpkMaterial.VertexColor(System.Numerics.Vector4.Zero).X+
                            ShpkMaterial.VertexColor(System.Numerics.Vector4.One).X)/2);
    }

    [TestMethod]
    public void LegacyTable_AllSixteenRowsHaveFourRgbaTexels()
    {
        var pixels = Enumerable.Range(0, 16 * 4).Select(i => new ShortVec4
        {
            X=ShortVec4.FromFloat(i/64f),Y=ShortVec4.FromFloat(.5f),Z=ShortVec4.FromFloat(.25f),W=ShortVec4.FromFloat(1)
        }).ToArray();
        var reader = new SpanBinaryReader(MemoryMarshal.AsBytes(pixels.AsSpan()));
        var table = new LegacyColorTable(ref reader);
        Assert.AreEqual((4,16),LegacyColorTable.TextureSize);
        var preview = table.ToTexture();
        for(var row=0;row<16;row++)
        for(var col=0;col<4;col++)
            Assert.AreEqual(pixels[row*4+col].ToSkColor(),preview[col,row]);
        Assert.AreEqual(LegacyColorTable.Size,reader.Position);
    }

    [TestMethod]
    public void RawIdentifiers_AreDecodedBeforeFloatSourceConversion()
    {
        var raw = new ColorTableRow { _idxData = new ShortVec4 { X=65535, W=64512, Y=ShortVec4.FromFloat(.5f) } };
        var node = JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(raw, new System.Text.Json.JsonSerializerOptions { IncludeFields=true,
            NumberHandling=System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals }));
        Assert.IsNotNull(node);
        Assert.AreEqual(raw.TileIndex,node["TileIndex"]!.GetValue<float>());
        Assert.AreEqual(65535,node["ShaderId"]!.GetValue<int>());
        Assert.AreEqual(64512,node["SphereIndex"]!.GetValue<int>());
        Assert.AreEqual(32,raw.TileIndex);
    }

    [TestMethod]
    public void MissingResourcesAndAliases_DoNotEnablePartialFormula()
    {
        foreach(var name in new[]{"skin.shpk","character.shpk","hair.shpk","iris.shpk","characterlegacy.shpk","characterstockings.shpk","myhair.shpk"})
        {
            var result = CharacterMaterial.Build(new JsonObject { ["ShaderPackage"]=name },new Dictionary<string,MaterialTexture>());
            Assert.IsNull(result.Graph);
            Assert.IsFalse(string.IsNullOrWhiteSpace(result.Gap));
            Assert.AreEqual("OPAQUE",result.AlphaMode);
        }
        Assert.AreEqual(CharacterShader.Basic,CharacterMaterial.Classify("myhair.shpk"));
    }

    [TestMethod]
    public void Hair_UsesTwoUvsAndPreservesPackedChannelsWithoutNormalCompression()
    {
        var textures = new Dictionary<string,MaterialTexture> {
            ["g_SamplerNormal"] = new("g_SamplerNormal","/Game/Test/N",false),
            ["g_SamplerMask"] = new("g_SamplerMask","/Game/Test/M",false)
        };
        var result=CharacterMaterial.Build(new JsonObject { ["ShaderPackage"]="hair.shpk" },textures);
        Assert.IsNotNull(result.Graph);
        Assert.AreEqual("MASK",result.AlphaMode);
        Assert.AreEqual(2,result.Graph.Nodes.OfType<MaterialUv>().Select(x=>x.Channel).Distinct().Count());
        Assert.AreEqual(3,result.Graph.Nodes.OfType<MaterialSample>().Count(x=>x.Path=="/Game/Test/N"));
        Assert.IsTrue(result.Graph.Nodes.OfType<MaterialSample>().All(x=>!x.Normal && !x.Srgb));
        Assert.AreEqual(6,result.Graph.Outputs.Count);
        var transparent=CharacterMaterial.Build(new JsonObject { ["ShaderPackage"]="hair.shpk",["IsTransparent"]=true },textures);
        Assert.AreEqual("MASK",transparent.AlphaMode);
        Assert.IsTrue(transparent.DitherOpacityMask);
    }

    [TestMethod]
    public void StructuredTable_RejectsIncompleteData()
    {
        Assert.IsNull(MaterialColorTable.Read(new JsonArray()));
        var rows=new JsonArray();
        for(var i=0;i<32;i++) rows.Add(new JsonObject());
        Assert.IsNull(MaterialColorTable.Read(rows));
    }

    [TestMethod]
    public void FloatTexture_RejectsWrongDimensions()
        => Assert.Throws<ArgumentException>(() => TextureWriter.WriteFloat("unused.uasset","/Game/Test/T",2,2,[1,2,3,4]));

    [TestMethod]
    public void Texture_BulkOffsetPointsToFirstPixelAfterFinalPackageLayout()
    {
        var file=Path.Combine(Path.GetTempPath(),"MeddleTextureOffset_"+Guid.NewGuid().ToString("N")+".uasset");
        try
        {
            float[] values=[4,.1234567f,65535,1];
            TextureWriter.WriteFloat(file,"/Game/Test/Float",1,1,values);
            var data=File.ReadAllBytes(file);
            var pixels=MemoryMarshal.AsBytes(values.AsSpan());
            var expectedOffset=data.AsSpan().IndexOf(pixels);
            Assert.IsTrue(expectedOffset>8);
            Assert.AreEqual((long)expectedOffset,BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(expectedOffset-8,8)));
            byte[] bgra=[23,47,81,123,54,98,143,201];
            TextureWriter.Write(file,"/Game/Test/Byte",2,1,bgra,false,linearData:true);
            data=File.ReadAllBytes(file);expectedOffset=data.AsSpan().IndexOf(bgra);
            Assert.IsTrue(expectedOffset>8);
            Assert.AreEqual((long)expectedOffset,BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(expectedOffset-8,8)));
        }
        finally { File.Delete(file); }
    }
}

