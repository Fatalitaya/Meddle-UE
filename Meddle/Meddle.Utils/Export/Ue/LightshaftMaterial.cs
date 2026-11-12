using System.Globalization;
using System.Text.Json.Nodes;
using Meddle.Unreal;

namespace Meddle.Utils.Export.Ue;

/// <summary>lightshaft.shpk PS0 with the static, authored-ray vertex adaptation.</summary>
public static class LightshaftMaterial
{
    public const string Reference = "FFXIV SM5 lightshaft.shpk PS0/VS0/VS1; docs/UE_MATERIAL_FORMULAS.md";
    private static float N(JsonObject d, string key, float fallback = 0, int i = 0)
        => (d[key] is JsonArray a ? a.ElementAtOrDefault(i) : d[key]) is JsonValue v && v.TryGetValue<float>(out var n) ? n : fallback;
    private static string F(float n) => n.ToString("R", CultureInfo.InvariantCulture);
    private static string Color(JsonObject d) => $"float3({F(N(d,"g_Color",1))},{F(N(d,"g_Color",1,1))},{F(N(d,"g_Color",1,2))})";

    public static MaterialFormulaResult Build(JsonObject d, IReadOnlyDictionary<string, MaterialTexture> textures)
    {
        MaterialFormulaResult Gap(string reason) => new(CharacterShader.Lightshaft,null,"ADDITIVE",reason,Unlit:true);
        var type = d["Type"]?.GetValue<string>() ?? "Type0";
        if (type is not ("Type0" or "Type1")) return Gap("Unverified lightshaft Type: " + type);
        var missing = new[] { "g_Sampler0", "g_Sampler1" }.Where(k=>!textures.ContainsKey(k)).ToArray();
        if (missing.Length != 0) return Gap("Missing lightshaft resources: " + string.Join(", ",missing));
        var g = new MaterialGraph();
        var position = g.Add(new MaterialWorldPosition());
        var normal = g.Add(new MaterialVertexNormal());
        var vertex = g.VertexColor();
        // This custom expression is evaluated through a VertexInterpolator: the VS overload
        // of TransformLocalVectorToWorld includes ISM transforms, unlike the PS overload.
        // Normalize each basis separately: SHPK uses rotate, not the scaled transform.
        string Axis(string key, int i, float fallback=0) => F(N(d,key,fallback,i));
        var projected = g.Custom("Lightshaft world projection (meters)",
            "float3 x=normalize(TransformLocalVectorToWorld(Parameters,float3(1,0,0)));\n" +
            "float3 y=normalize(TransformLocalVectorToWorld(Parameters,float3(0,1,0)));\n" +
            "float3 z=normalize(TransformLocalVectorToWorld(Parameters,float3(0,0,1)));\n" +
            // XIV (x,y,z) -> UE (-z,x,y), also for the projection covectors.
            $"float3 u=-({Axis("g_TexU",2,-1)})*x+({Axis("g_TexU",0)})*y+({Axis("g_TexU",1)})*z;\n" +
            $"float3 v=-({Axis("g_TexV",2)})*x+({Axis("g_TexV",0)})*y+({Axis("g_TexV",1,-1)})*z;\n" +
            "return float2(dot(u,world),dot(v,world))*0.01;",2,("world",position));
        var uv = g.Add(new MaterialVertexInterpolator(projected));
        var angle = g.Custom("Lightshaft vertex angular attenuation",
            $"float a=dot(ResolvedView.ViewForward,normal); return alpha*(a*a*(1-({F(N(d,"g_AngleClip"))}))+({F(N(d,"g_AngleClip"))}));",
            1,("normal",normal),("alpha",vertex with { Mask=8 }));
        var attenuation = g.Add(new MaterialVertexInterpolator(angle));
        var t0 = g.Sample(textures["g_Sampler0"].Package,false,g.Uv(0));
        var t1 = g.Sample(textures["g_Sampler1"].Package,false,uv);
        g.Outputs["EmissiveColor"] = g.Custom("Lightshaft PS0 RGB product",
            $"return tex0.rgb*tex1.rgb*{Color(d)}*attenuation;",3,("tex0",t0),("tex1",t1),("attenuation",attenuation));
        // PS0 writes zero alpha, but the game effect adds RGB. UE Additive multiplies
        // emissive by opacity, so opacity must be one; neither texture alpha is coverage.
        g.Outputs["Opacity"] = g.Custom("Lightshaft additive weight","return 1;",1);
        return new(CharacterShader.Lightshaft,g,"ADDITIVE",null,Unlit:true,Adaptation:
            "Static authored-ray adaptation: PS0 RGB product, vertex alpha/angular attenuation and instance-rotated world projection. " +
            "Animation phase is zero; game instance tint, near-clip fade and exposure multiplier are unity. " +
            (type=="Type1" ? "Type1 sunlight-driven vertex deformation, normal rotation, sun color and directional attenuation require runtime light buffers and are not reproduced." : ""));
    }
}
