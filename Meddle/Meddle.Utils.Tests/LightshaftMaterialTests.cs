using System.Text.Json.Nodes;
using Meddle.Unreal;
using Meddle.Utils.Export.Ue;

namespace Meddle.Utils.Tests;

[TestClass]
public sealed class LightshaftMaterialTests
{
    [TestMethod]
    public void OpaqueFlagDoesNotTurnLightshaftIntoOpaqueSurface()
    {
        var data=new JsonObject { ["ShaderPackage"]="lightshaft.shpk",["IsTransparent"]=false,["Type"]="Type1" };
        var textures=new[] { "g_Sampler0", "g_Sampler1" }.ToDictionary(x=>x,x=>new MaterialTexture(x,"/Game/Test/"+x,true));
        var formula=CharacterMaterial.Build(data,textures);
        Assert.AreEqual("ADDITIVE",formula.AlphaMode);
        Assert.IsTrue(formula.Unlit);
        Assert.IsNotNull(formula.Graph);
        Assert.IsTrue(formula.Graph.Nodes.OfType<MaterialSample>().All(t=>!t.Srgb));
        Assert.AreEqual(2,formula.Graph.Nodes.OfType<MaterialVertexInterpolator>().Count());
        StringAssert.Contains(formula.Adaptation!,"Type1 sunlight");
        textures.Remove("g_Sampler1");
        var missing=CharacterMaterial.Build(data,textures);
        Assert.IsNull(missing.Graph);
        Assert.AreEqual("ADDITIVE",missing.AlphaMode);
        StringAssert.Contains(missing.Gap!,"g_Sampler1");
    }
}
