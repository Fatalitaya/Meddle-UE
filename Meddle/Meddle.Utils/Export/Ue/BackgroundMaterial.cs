using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Meddle.Unreal;

namespace Meddle.Utils.Export.Ue;

/// <summary>Dry surface attributes decoded from bg/terrain/bgprop/bgcolorchange SM5 GBuffer passes.</summary>
public static class BackgroundMaterial
{
    public const string Reference = "FFXIV SM5 background GBuffer; docs/UE_MATERIAL_FORMULAS.md";
    private static float N(JsonObject d, string k, float fallback = 0, int i = 0)
        => (d[k] is JsonArray a ? a.ElementAtOrDefault(i) : d[k]) is JsonValue v && v.TryGetValue<float>(out var n) ? n : fallback;
    private static string F(float n) => n.ToString("R", CultureInfo.InvariantCulture);
    private static string V(JsonObject d, string k, float fallback = 0)
        => $"float3({F(N(d,k,fallback))},{F(N(d,k,fallback,1))},{F(N(d,k,fallback,2))})";

    public static MaterialFormulaResult Build(JsonObject d, IReadOnlyDictionary<string, MaterialTexture> textures)
    {
        var name = d["ShaderPackage"]?.GetValue<string>();
        var dye = name == "bgcolorchange.shpk";
        var alphaTest = d["ApplyAlphaTest"]?.GetValue<string>() == "ApplyAlphaTestOn";
        var mode = d["GetValues"]?.GetValue<string>() ?? "GetSingleValues";
        var multi = mode is "GetMultiValues" or "GetAlphaMultiValues" or "0x941820BE";
        var height = mode is "GetAlphaMultiValues" or "0x941820BE";
        var vertex = d["ApplyVertexColor"]?.GetValue<string>() == "ApplyVertexColorOn";
        var alpha = d["IsTransparent"]?.GetValue<bool>() == true ? "BLEND" : alphaTest ? "MASK" : "OPAQUE";
        MaterialFormulaResult Gap(string reason) => new(CharacterShader.Background, null, alpha, reason);
        if (name is not ("bg.shpk" or "terrain.shpk" or "bgskin.shpk" or "bgprop.shpk" or "bgcolorchange.shpk"))
            return Gap("Background shader requires a separate UV/crest formula; raw parameters are retained.");
        if (dye && alphaTest) return Gap("bgcolorchange has no verified alpha-test-on deferred pass.");
        if (d["IsTransparent"]?.GetValue<bool>() == true)
            return Gap("Forward transparent background pass is not the verified deferred surface pass.");
        if (mode is not ("GetSingleValues" or "GetMultiValues" or "GetAlphaMultiValues" or "0x941820BE"))
            return Gap("Unverified background GetValues: " + mode);
        if (d["0x36F72D5F"] is JsonValue detail && detail.GetValue<string>() != "0x88A3965A")
            return Gap("Procedural/detail background sampling requires noise and camera-dependent resources.");
        var required = new List<string> { "g_SamplerColorMap0", "g_SamplerSpecularMap0" };
        if (multi) required.AddRange(["g_SamplerColorMap1", "g_SamplerSpecularMap1"]);
        if (height) required.AddRange(["g_SamplerNormalMap0", "g_SamplerNormalMap1"]);
        if (dye) required.Add("g_SamplerNormalMap0");
        var missing = required.Where(k => !textures.ContainsKey(k)).ToArray();
        if (missing.Length != 0) return Gap("Missing background resources: " + string.Join(", ", missing));

        var g = new MaterialGraph();
        var inputs = new List<(string Name, MaterialLink Link)> { ("vertex", g.VertexColor()) };
        for (var layer = 0; layer < (multi ? 2 : 1); layer++)
        {
            var uv = g.Uv(layer);
            // bgcolorchange VS passes the authored UV unchanged; ordinary bg scales it in PS.
            MaterialLink Coords(string scale) => dye ? uv : g.Custom("Background UV", $"return uv*float2({F(N(d,"g_NormalUVScale",1,layer*2)*N(d,scale,1,layer*2))},{F(N(d,"g_NormalUVScale",1,layer*2+1)*N(d,scale,1,layer*2+1))});",2,("uv",uv));
            void Sample(string symbol, string usage, MaterialLink coords)
            {
                if (textures.TryGetValue(usage, out var t)) inputs.Add((symbol, g.Sample(t.Package, false, coords)));
            }
            Sample("c"+layer,"g_SamplerColorMap"+layer,Coords("g_ColorUVScale"));
            Sample("s"+layer,"g_SamplerSpecularMap"+layer,Coords("g_SpecularUVScale"));
            var normalUv = dye ? uv : g.Custom("Background normal UV",$"return uv*float2({F(N(d,"g_NormalUVScale",1,layer*2))},{F(N(d,"g_NormalUVScale",1,layer*2+1))});",2,("uv",uv));
            Sample("n"+layer,"g_SamplerNormalMap"+layer,normalUv);
        }
        var code = new StringBuilder("float w=0;\n");
        if (multi) code.AppendLine("w=saturate(vertex.a);");
        if (height) code.AppendLine($"float a=vertex.a-0.5; w=saturate(lerp(n0.a,n1.a,{F(N(d,"g_AlphaMultiParam",0,1))})+saturate(abs(2*a)-{F(N(d,"g_AlphaMultiParam"))})*{F(N(d,"g_AlphaMultiParam",0,3))}*sign(a));");
        code.AppendLine($"float3 base=c0.rgb*{V(d,"g_DiffuseColor",1)}, sp={V(d,"g_SpecularColor",1)}; float4 spec=s0; float opacity=c0.a;");
        if (multi) code.AppendLine($"base=lerp(base,c1.rgb*{V(d,"g_MultiDiffuseColor",1)},w); sp=lerp(sp,{V(d,"g_MultiSpecularColor",1)},w); spec=lerp(s0,s1,w); opacity=lerp(c0.a,c1.a,w);");
        if (vertex) code.AppendLine("base=max(base+vertex.rgb-0.5,0);");
        code.AppendLine("base=base*base;");
        if (dye)
        {
            var stain = d.ContainsKey("BgStainLinear") ? V(d,"BgStainLinear",1) : $"({V(d,"StainColor",1)}*{V(d,"StainColor",1)})";
            code.AppendLine($"base=c0.rgb*c0.rgb*lerp(float3(1,1,1),{stain},c0.a);");
        }
        code.AppendLine($"float3 e={V(d,"g_EmissiveColor")};");
        if (d.ContainsKey("LayoutColor"))
            code.AppendLine($"e=dot(e,float3(1,1,1))>0 ? lerp(e,{V(d,"LayoutColor")}*{V(d,"LayoutColor")},{F(N(d,"LayoutColor",0,3))}) : float3(0,0,0);");
        else code.AppendLine("e=dot(e,float3(1,1,1))>0 ? e : float3(0,0,0);");
        code.AppendLine($"float3 emission={(dye ? "base" : "c0.rgb*c0.rgb")}*e;");
        if (multi) code.AppendLine($"emission=lerp(emission,c1.rgb*c1.rgb*{V(d,"g_MultiEmissiveColor")},w);");
        else code.AppendLine("emission*=s0.a;");
        code.AppendLine($"emission*={F(N(d,"BgEmissivePower",1))};");
        code.AppendLine("float3 normal=float3(0,0,1);");
        var normal0 = textures.ContainsKey("g_SamplerNormalMap0");
        if (normal0)
        {
            code.AppendLine($"float2 xy=n0.rg-0.5; normal=normalize(float3(xy*{F(N(d,"g_NormalScale",1))},sqrt(max(0.25-dot(xy,xy),0.000001))));");
            if (multi && textures.ContainsKey("g_SamplerNormalMap1"))
                code.AppendLine($"xy=n1.rg-0.5; normal=normalize(lerp(normal,normalize(float3(xy*{F(N(d,"g_MultiNormalScale",1))},sqrt(max(0.25-dot(xy,xy),0.000001)))),w));");
            code.AppendLine(d["SourceTangentBasis"]?.GetValue<string>() == "FFXIVBinormal" ? "normal=normal.yxz*float3(1,-1,1);" : "normal*=float3(1,-1,1);");
        }
        void Output(string key, string expression, int dim) => g.Outputs[key] = g.Custom("Background "+key,code+"return "+expression+";",dim,inputs.ToArray());
        Output("BaseColor","base",3);
        Output("Normal","normal",3);
        Output("Roughness","spec.g",1);
        Output("Specular","0.5*spec.r*saturate(sp.r)",1);
        Output("Metallic","0",1);
        Output("EmissiveColor","emission",3);
        Output("Opacity",alphaTest ? $"opacity>={F(N(d,"g_AlphaThreshold",.5f))} ? 1 : 0" : "1",1);
        var coloredSpec = Enumerable.Range(0,multi ? 2 : 1).Any(i => {
            var key=i==0 ? "g_SpecularColor" : "g_MultiSpecularColor";
            return N(d,key,1)!=N(d,key,1,1) || N(d,key,1)!=N(d,key,1,2);
        });
        return new(CharacterShader.Background,g,alpha,null,Adaptation:
            "Dry GBuffer surface; game lighting, wetness and animation are not reproduced. " +
            (coloredSpec ? "Colored specular uses the red-channel scalar approximation in UE Default Lit. " : "") +
            (multi && normal0 ? "Layered normals share the exported tangent basis; independently rotated second-layer tangents are not retained. " : "") +
            "Layout RGB squared and Alpha control primary emissive color/blend; instance power scales emission.");
    }
}
