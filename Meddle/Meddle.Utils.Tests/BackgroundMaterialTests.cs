using System.Text.Json.Nodes;
using Meddle.Unreal;
using Meddle.Utils.Export.Ue;

namespace Meddle.Utils.Tests;

[TestClass]
public sealed class BackgroundMaterialTests
{
    private static Dictionary<string,MaterialTexture> Textures(params string[] usages) => usages.ToDictionary(x=>x,x=>new MaterialTexture(x,"/Game/Test/"+x,false));

    [TestMethod]
    public void LayeredBackgroundRequiresBothColorAndSpecularResources()
    {
        var data=new JsonObject { ["ShaderPackage"]="bg.shpk", ["GetValues"]="GetMultiValues" };
        var textures=Textures("g_SamplerColorMap0","g_SamplerSpecularMap0","g_SamplerColorMap1");
        var result=CharacterMaterial.Build(data,textures);
        Assert.IsNull(result.Graph);
        StringAssert.Contains(result.Gap!,"g_SamplerSpecularMap1");
        textures.Add("g_SamplerSpecularMap1",new("g_SamplerSpecularMap1","/Game/Test/S1",false));
        Assert.IsNotNull(CharacterMaterial.Build(data,textures).Graph);
    }

    [TestMethod]
    public void UnverifiedDetailAndForwardPassAreReportedRatherThanMarkedVerified()
    {
        var data=new JsonObject { ["ShaderPackage"]="bg.shpk", ["0x36F72D5F"]="0x1E314009" };
        var textures=Textures("g_SamplerColorMap0","g_SamplerSpecularMap0");
        Assert.IsNull(CharacterMaterial.Build(data,textures).Graph);
        data.Remove("0x36F72D5F");data["IsTransparent"]=true;
        Assert.IsNull(CharacterMaterial.Build(data,textures).Graph);
    }

    [TestMethod]
    public void BackgroundSamplesGammaEncodedColorsWithoutHardwareSrgbConversion()
    {
        var data=new JsonObject { ["ShaderPackage"]="bg.shpk" };
        var textures=Textures("g_SamplerColorMap0","g_SamplerSpecularMap0");
        var graph=CharacterMaterial.Build(data,textures).Graph!;
        Assert.IsNotNull(graph);
        Assert.IsTrue(graph.Nodes.OfType<MaterialSample>().All(s=>!s.Srgb));
    }
}
