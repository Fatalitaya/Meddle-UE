using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Meddle.Unreal;

namespace Meddle.Utils.Export.Ue;

/// <summary>Dry skin surface attributes traced from SM5 skin.shpk; see UE_SKIN_REVERSE.md.</summary>
public static class SkinMaterial
{
    public const string Reference = "FFXIV SM5 skin.shpk 2026-11-12; tools/ShpkProbe";
    private static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
    private static float N(JsonObject d, string key, float fallback = 0, int index = 0)
        => (d[key] is JsonArray a ? a.ElementAtOrDefault(index) : d[key])?.GetValue<float>() ?? fallback;
    private static string C(JsonObject d, string key, float fallback = 1)
        => "float3(" + string.Join(",", Enumerable.Range(0,3).Select(i=>F(N(d,key,fallback,i)))) + ")";

    public static MaterialFormulaResult Build(JsonObject data, IReadOnlyDictionary<string,MaterialTexture> textures)
    {
        var branch = data["GetMaterialValue"]?.GetValue<string>() ?? "GetMaterialValueFace";
        var face = branch is "GetMaterialValueFace" or "GetMaterialValueFace2";
        var body = branch == "GetMaterialValueBody";
        var jjm = branch == "GetMaterialValueBodyJJM";
        var emissive = branch == "GetMaterialValueFaceEmissive";
        var transparent = data["IsTransparent"]?.GetValue<bool>() == true;
        MaterialFormulaResult Gap(string reason) => new(CharacterShader.Skin,null,transparent ? "BLEND" : "OPAQUE",reason);
        if (!(face || body || jjm || emissive)) return Gap("Unverified skin GetMaterialValue: " + branch);
        string[] required = ["g_SamplerDiffuse","g_SamplerNormal","g_SamplerMask","chara_tile_norm_array","chara_tile_orb_array"];
        var missing = required.Where(k=>!textures.ContainsKey(k)).ToArray();
        if (missing.Length>0) return Gap("Missing required resources: " + string.Join(", ",missing));
        var g = new MaterialGraph();
        var uv = g.Uv(0); var uv1 = g.Uv(1);
        var bias = g.Custom("Skin mip bias", $"return {F(N(data,"g_TextureMipBias"))};",1);
        var inputs = new List<(string Name,MaterialLink Link)> { ("vertex",g.VertexColor()) };
        void Sample(string name,string usage,MaterialLink coords,MaterialLink? mip = null)
            => inputs.Add((name,g.Sample(textures[usage].Package,textures[usage].Srgb,coords,mipBias:mip)));
        Sample("d","g_SamplerDiffuse",uv,bias); Sample("n","g_SamplerNormal",uv,bias); Sample("m","g_SamplerMask",uv,bias);
        // Faces tile in UV1; Body/BodyJJM in UV0. Flattened array slices retain game V.
        var tile = g.Custom("SHPK skin tile UV",$"float2 p=frac(uv*float2({F(N(data,"g_TileScale",16))},{F(N(data,"g_TileScale",16,1))})); return float2(p.x,(clamp(trunc({F(N(data,"g_TileIndex"))}),0,63)+p.y)/64);",2,("uv",body || jjm ? uv : uv1));
        var tileBias = g.Custom("Skin tile mip bias",$"return max(log2(min({F(N(data,"g_TileScale",16))},{F(N(data,"g_TileScale",16,1))})/128),0)+{F(N(data,"g_TileMipBiasOffset"))};",1);
        Sample("tn","chara_tile_norm_array",tile,tileBias); Sample("tileOrb","chara_tile_orb_array",tile,tileBias);
        var code = new StringBuilder(Helpers);
        // Exporter applies the VS RGB square before interpolation. Alpha is unmodified.
        code.AppendLine($"float strength=n.b*{F(N(data,"g_TileAlpha",1))}*vertex.a{(body ? "*n.a" : jjm ? "*(m.b>=1 ? 0 : 1)" : "")};");
        code.AppendLine("float4 orb=lerp(float4(1,0.5,1,1),tileOrb,saturate(strength));");
        code.AppendLine($"float3 b=f.normal(n.rg,lerp(1,{F(N(data,"MuscleTone",1))},vertex.r)); float3 t=f.normal(tn.rg,strength)*float3(-1,-1,1); b.z+=1; float3 normal=normalize(b*dot(b,t)/b.z-t);");
        code.AppendLine(data["SourceTangentBasis"]?.GetValue<string>() == "FFXIVBinormal"
            ? "normal=normal.yxz*float3(1,-1,1);" : "normal.y=-normal.y;");
        code.AppendLine($"float lip={(face ? "n.a*"+F(N(data,"LipColor",0,3)) : "0")}; float r=m.g{(face ? "*lerp(1,"+F(N(data,"g_LipRoughnessScale",.7f))+",lip)" : "")};");
        code.AppendLine("float roughness=orb.g<=0.5 ? (1-2*orb.g)*r*r+2*orb.g*r : 2*(1-orb.g)*r+(2*orb.g-1)*sqrt(max(r,0));");
        // GBuffer1.R is a reflection multiplier, NOT F0. Dry dielectric F0=0.04;
        // lighting expands the reserved 254/255 scalar range. UE F0=0.08*Specular.
        code.AppendLine("float specular=0.5*saturate(m.r*vertex.b*orb.a*(255.0/254.0));");
        code.AppendLine($"float3 tint=lerp(float3(1,1,1),{C(data,"SkinColor")},n.b);");
        if (jjm) code.AppendLine($"tint=lerp(lerp(float3(1,1,1),lerp({C(data,"MainColor")},{C(data,"MeshColor")},m.a),n.a),{C(data,"SkinColor")},n.b);");
        // Native exports store encoded diffuse as linear data, then square as SHPK does.
        // An sRGB source supplied by another caller is explicitly re-encoded first.
        code.AppendLine(textures["g_SamplerDiffuse"].Srgb ? "float3 diffuse=f.encode(d.rgb);" : "float3 diffuse=d.rgb;");
        code.AppendLine("float3 baseColor=diffuse*diffuse*tint;");
        if (face) code.AppendLine($"baseColor=lerp(baseColor,{C(data,"LipColor",0)},lip);");
        code.AppendLine("baseColor*=orb.b;");
        if (textures.ContainsKey("Decal"))
        {
            var decalUv = body || jjm ? uv1 : g.Custom("Skin face paint UV",$"return float2(uv.x*{F(N(data,"FacePaintUVMultiplier"))}+{F(N(data,"FacePaintUVOffset"))},uv.y);",2,("uv",uv1));
            Sample("decal","Decal",decalUv);
            code.AppendLine($"baseColor=lerp(baseColor,{C(data,"DecalColor",0)},decal.r*{F(N(data,"DecalColor",0,3))});");
        }
        code.AppendLine($"baseColor*={C(data,"g_DiffuseColor")}*{C(data,"g_DiffuseColor")};");
        code.AppendLine($"float3 emission={(emissive ? C(data,"g_EmissiveColor",0)+"*"+C(data,"g_EmissiveColor",0)+"*n.a" : "float3(0,0,0)")};");
        var alpha = jjm ? "1.0" : "d.a";
        var threshold = F(N(data,"g_AlphaThreshold"));
        code.AppendLine(transparent ? $"float opacity={threshold}>0 ? saturate({alpha}/{threshold}) : {alpha};" : $"float opacity={alpha}>={threshold} ? 1 : 0;");
        foreach (var (name,expression,dimension) in new[] { ("BaseColor","baseColor",3),("Normal","normal",3),("Roughness","roughness",1),("Specular","specular",1),("Metallic","0",1),("EmissiveColor","emission",3),("Opacity","opacity",1) })
            g.Outputs[name]=g.Custom("SHPK Skin " + name,code+"\nreturn "+expression+";",dimension,inputs.ToArray());
        return new(CharacterShader.Skin,g,transparent ? "BLEND" : "MASK",null,
            Adaptation:"SHPK dry skin attributes and dielectric reflection adapted to UE Default Lit. Game SSS, wetness, lighting-dependent emissive gain and animated Face2 wrinkle weights are not reproduced.");
    }

    private const string Helpers = """
        struct SkinFormula {
            float3 normal(float2 rg,float strength) { float2 p=rg-0.5; return normalize(float3(p*strength,sqrt(max(0.25-dot(p,p),0.000001)))); }
            float3 encode(float3 c) { return float3(c.r<=0.0031308?12.92*c.r:1.055*pow(c.r,1.0/2.4)-0.055,c.g<=0.0031308?12.92*c.g:1.055*pow(c.g,1.0/2.4)-0.055,c.b<=0.0031308?12.92*c.b:1.055*pow(c.b,1.0/2.4)-0.055); }
        } f;
        """;
}
