using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using Meddle.Unreal;

namespace Meddle.Utils.Export.Ue;

public enum CharacterShader { Basic, Equipment, Skin, Hair, Iris, Legacy, Tattoo, Occlusion }
public sealed record MaterialTexture(string Usage, string Package, bool Srgb, bool Nearest = false, bool Clamp = false, int Width = 1, int Height = 1);
public sealed record MaterialFormulaResult(CharacterShader Shader, MaterialGraph? Graph, string AlphaMode, string? Gap,
    bool Unlit = false, string? Adaptation = null, bool DitherOpacityMask = false);

/// <summary>Attribute arithmetic from MeddleTools 56aa1252. No BSDF/IOR conversion.</summary>
public static class CharacterMaterial
{
    public const string ReferenceCommit = "56aa1252ba341fbc732df6e79b95e388d2057ba0";
    public const string HairReference = "FFXIV SM5 hair.shpk normal/coverage 2026-11-12; tools/ShpkProbe";
    public static CharacterShader Classify(string shader) => shader switch
    {
        "character.shpk" => CharacterShader.Equipment,
        "skin.shpk" => CharacterShader.Skin,
        "hair.shpk" => CharacterShader.Hair,
        "iris.shpk" => CharacterShader.Iris,
        "characterlegacy.shpk" => CharacterShader.Legacy,
        "charactertattoo.shpk" => CharacterShader.Tattoo,
        "characterocclusion.shpk" => CharacterShader.Occlusion,
        _ => CharacterShader.Basic
    };

    public static bool IsColor(string usage) => usage is "g_SamplerDiffuse" or "g_SamplerSkinDiffuse" or
        "g_SamplerColorMap0" or "g_SamplerColorMap1" or "g_Sampler0" or "g_Sampler1";

    private static string F(float value) => value.ToString("R", CultureInfo.InvariantCulture);
    private static float Number(JsonNode? node, float fallback = 0) => node is JsonValue v && v.TryGetValue<float>(out var x) ? x : fallback;
    private static float N(JsonObject data, string key, float fallback = 0, int index = 0)
        => Number(data[key] is JsonArray a ? a.ElementAtOrDefault(index) : data[key], fallback);
    private static string V(JsonObject data, string key, Vector3 fallback)
        => $"float3({F(N(data, key, fallback.X))},{F(N(data, key, fallback.Y, 1))},{F(N(data, key, fallback.Z, 2))})";
    private static bool Key(JsonObject data, string key, string value) => data[key]?.GetValue<string>() == value;

    public static MaterialFormulaResult Build(JsonObject data, IReadOnlyDictionary<string, MaterialTexture> textures)
    {
        var shader = Classify(data["ShaderPackage"]?.GetValue<string>() ?? "");
        if (shader is CharacterShader.Legacy or CharacterShader.Tattoo or CharacterShader.Occlusion)
            return ShpkMaterial.Build(shader, data, textures);
        var compatibility = Key(data,"GetValues","GetValuesCompatibility") || Key(data,"GetValuesTextureType","Compatibility");
        var transparent = data["IsTransparent"]?.GetValue<bool>() == true;
        MaterialFormulaResult Gap(string reason) => new(shader, null, transparent ? "BLEND" : "OPAQUE", reason);
        if (shader == CharacterShader.Basic) return Gap("Shader variant has no verified attribute mapping.");
        string[] required = shader == CharacterShader.Hair ? ["g_SamplerNormal", "g_SamplerMask"] :
            shader == CharacterShader.Equipment ? ["g_SamplerNormal", "g_SamplerMask", "g_SamplerIndex", "chara_tile_norm_array", "chara_tile_orb_array"] :
            shader == CharacterShader.Skin ? ["g_SamplerDiffuse", "g_SamplerNormal", "g_SamplerMask", "chara_tile_norm_array", "chara_tile_orb_array"] :
            ["g_SamplerDiffuse", "g_SamplerNormal", "g_SamplerMask"];
        if (shader == CharacterShader.Equipment && compatibility) required = [..required, "g_SamplerDiffuse"];
        var missing = required.Where(k => !textures.ContainsKey(k)).ToArray();
        if (missing.Length != 0) return Gap("Missing required resources: " + string.Join(", ", missing));
        var table = MaterialColorTable.Read(data["ColorTable"]?["ColorTable"]?["Rows"]);
        if (shader == CharacterShader.Equipment && table == null)
            return Gap("Modern structured color table requires 32 rows; legacy table mapping is not enabled.");

        var g = new MaterialGraph();
        var uv = g.Uv(0);
        var uv1 = g.Uv(1);
        var inputs = new List<(string Name, MaterialLink Link)>();
        MaterialLink? textureBias = shader == CharacterShader.Hair ? g.Custom("Hair texture mip bias",$"return {F(N(data,"g_TextureMipBias"))};",1) : null;
        MaterialLink Sample(string usage, MaterialLink coords) => g.Sample(textures[usage].Package, textures[usage].Srgb, coords, mipBias:textureBias);
        void Add(string name, string usage, MaterialLink coords) => inputs.Add((name, Sample(usage, coords)));
        Add("n", "g_SamplerNormal", uv);
        Add("m", "g_SamplerMask", uv);
        if (shader != CharacterShader.Hair && (shader != CharacterShader.Equipment || compatibility)) Add("d", "g_SamplerDiffuse", uv);
        var code = new StringBuilder(Helpers);
        code.AppendLine("float3 baseColor=0, emission=0; float roughness=0.6, metallic=0, opacity=1;");
        code.AppendLine("float3 normal = f.normalFix(n);");
        var threshold = F(N(data, "g_AlphaThreshold", shader == CharacterShader.Hair ? 1 : .5f));
        if (shader == CharacterShader.Hair)
        {
            Add("n1", "g_SamplerNormal", uv1);
            inputs.Add(("vertex", g.VertexColor()));
            var face = Key(data, "GetSubColor", "GetSubColorFace");
            var alphaBias = g.Custom("Hair alpha mip bias",$"return {F(N(data,"g_TextureMipBias")-4)};",1);
            inputs.Add(("hairCoverage",g.Sample(textures["g_SamplerNormal"].Package,false,face ? uv : uv1,mipBias:alphaBias)));
            // SHPK PS1/PS7: Z is reconstructed BEFORE applying g_NormalScale to XY.
            code.AppendLine($"float2 hairXY=n.rg-0.5; float3 hairNormal=normalize(float3(hairXY*{F(N(data,"g_NormalScale",1))},sqrt(max(0.25-dot(hairXY,hairXY),0.000001))));");
            // MeshBuilder carries XIV's authored binormal in glTF's tangent slot.
            // Including the handedness conversion, UE's tangent Y is -game tangent X.
            code.AppendLine(Key(data,"SourceTangentBasis","FFXIVBinormal")
                ? "normal=hairNormal.yxz*float3(1,-1,1);" : "normal=hairNormal*float3(1,-1,1);");
            code.AppendLine($"baseColor=lerp({V(data,"MainColor",Vector3.One)}, {(face ? "float3(0,0,0)" : V(data,"MeshColor",Vector3.One))}, saturate({(face ? "n.b" : "n1.b")})) * f.reinhard({V(data,"g_DiffuseColor",Vector3.Zero)}) * m.a;");
            code.AppendLine($"roughness=m.r; float hairAlpha=hairCoverage.a*lerp(vertex.a,1.0,{F(N(data,"0xAD94E254"))});");
            code.AppendLine(transparent
                ? $"opacity=hairAlpha<1.0/255.0 ? 0 : ({threshold}>0 ? saturate(hairAlpha/{threshold}) : saturate(hairAlpha));"
                : $"opacity=hairAlpha>={threshold} ? 1 : 0;");
        }
        if (shader == CharacterShader.Iris)
        {
            inputs.Add(("vertex", g.VertexColor())); inputs.Add(("uv", uv));
            code.AppendLine($"float3 eye=vertex.r>vertex.g ? {V(data,"LeftIrisColor",new(.65f,.25f,.27f))} : {V(data,"RightIrisColor",new(.30865854f,.29908133f,.6004111f))};");
            code.AppendLine($"baseColor=d.rgb*lerp({V(data,"g_WhiteEyeColor",new(.77f))},eye,saturate(m.b)); roughness=0.2;");
            var radius = data.ContainsKey("unk_LimbalRingRange") ? "unk_LimbalRingRange" : "g_IrisRingUvRadius";
            var fade = data.ContainsKey("unk_LimbalRingFade") ? "unk_LimbalRingFade" : "g_IrisRingUvFadeWidth";
            code.AppendLine($"float r=min(length(uv-float2(0.5,0.5)),1.0), lo={F(N(data,radius))}, hi={F(N(data,radius,0,1))};");
            code.AppendLine($"float start=saturate(lo-{F(N(data,fade))}), end=saturate(hi+{F(N(data,fade,0,1))});");
            code.AppendLine("float ring=saturate((r<hi && r>=start ? f.map(r,start,lo) : 0) + (r<=end && r>lo ? 1-f.map(r,hi,end) : 0));");
            code.AppendLine($"emission={V(data,"g_IrisRingColor",Vector3.Zero)} * ring * {F(N(data,"g_IrisRingEmissiveIntensity"))} * (vertex.r>vertex.g ? {F(N(data,"LeftIrisColor",0,3))} : {F(N(data,"RightIrisColor",0,3))});");
        }
        if (shader is CharacterShader.Skin or CharacterShader.Equipment)
        {
            var tileCode = new StringBuilder(Helpers);
            var tileInputs = new List<(string Name, MaterialLink Link)> { ("uv", uv) };
            if (shader == CharacterShader.Equipment)
            {
                var index = Sample("g_SamplerIndex", uv); inputs.Add(("idx", index)); tileInputs.Add(("idx", index));
                var tableCode = TableCode(table!);
                code.Append(tableCode); tileCode.Append(tableCode);
                code.AppendLine($"baseColor=tableColor{(compatibility ? "*d.rgb" : "")}; metallic=packed.x; roughness=saturate(packed.y*m.g); emission=tableEmission;");
                code.AppendLine($"opacity=f.alphaDiv(n.b,{threshold});");
            }
            else
            {
                code.AppendLine($"float tileAlpha={F(N(data,"g_TileAlpha",1))};");
                tileCode.AppendLine($"float tileIndex={F(N(data,"g_TileIndex"))}; float2 tileScale=float2({F(N(data,"g_TileScale",16))},{F(N(data,"g_TileScale",16,1))});");
                var face = Key(data,"GetMaterialValue","GetMaterialValueFace");
                var body = Key(data,"GetMaterialValue","GetMaterialValueBodyJJM");
                var emissive = Key(data,"GetMaterialValue","GetMaterialValueFaceEmissive");
                code.AppendLine($"float3 skin={V(data,"SkinColor",Vector3.One)}, emissiveColor={V(data,"g_EmissiveColor",Vector3.Zero)};");
                code.AppendLine($"baseColor=lerp(f.reinhard({V(data,"g_DiffuseColor",Vector3.One)}),skin,saturate(n.b));");
                if (emissive) code.AppendLine("baseColor=lerp(baseColor,emissiveColor,saturate(f.bw(emissiveColor)*n.a)); emission=emissiveColor*n.a;");
                if (face) code.AppendLine($"baseColor=lerp(baseColor,{V(data,"LipColor",Vector3.Zero)},saturate({F(N(data,"LipColor",0,3))}*n.a));");
                if (body) code.AppendLine($"baseColor=lerp(baseColor,lerp(lerp({V(data,"MainColor",Vector3.Zero)},{V(data,"MeshColor",Vector3.Zero)},saturate(m.a)),skin,saturate(n.b)),saturate(n.a));");
                code.AppendLine($"float a={(body ? "1.0" : "d.a")}; baseColor=lerp(d.rgb,d.rgb*baseColor,saturate(a)); roughness=m.g; opacity=a*(a>{threshold} ? 1.0 : 0.0);");
            }
            // glTF/UE V runs downward; Blender UVMap runs upward. Convert before and after tile_select.
            tileCode.AppendLine("float2 b=fmod(float2(uv.x,1-uv.y)*tileScale,1.0); return float2(b.x,1-(64-floor(tileIndex)-1+b.y)/64);");
            var tileUv = g.Custom("tile_select / ApplyUVScale", tileCode.ToString(), 2, tileInputs.ToArray());
            Add("tn", "chara_tile_norm_array", tileUv); Add("orb", "chara_tile_orb_array", tileUv);
            if (shader == CharacterShader.Equipment)
                code.AppendLine("baseColor *= saturate(orb.b);");
            code.AppendLine("normal += f.normalFix(tn)*tn.a*tileAlpha;");
            if (textures.ContainsKey("Decal"))
            {
                var decalUv = shader == CharacterShader.Skin ? g.Custom("meddle decaluv", $"return float2(uv.x*{F(N(data,"FacePaintUVMultiplier"))}+{F(N(data,"FacePaintUVOffset"))},uv.y);", 2, ("uv",uv1)) : uv1;
                Add("decal", "Decal", decalUv); inputs.Add(("decalUv", decalUv));
                var decalTexture = textures["Decal"];
                // Blender CLIP is a black-border sampler. Clamp samples multiplied by
                // bilinear border coverage retain the half-texel transition on both sides.
                code.AppendLine($"float2 border=saturate(min(decalUv,1-decalUv)*float2({decalTexture.Width},{decalTexture.Height})+0.5); float coverage=border.x*border.y;");
                code.AppendLine(shader == CharacterShader.Skin
                    ? $"baseColor=lerp(baseColor,{V(data,"DecalColor",Vector3.Zero)},saturate(f.bw(decal.rgb)*{F(N(data,"DecalColor",0,3))}*coverage));"
                    : "baseColor=lerp(baseColor,decal.rgb*coverage,saturate(decal.a*coverage));");
            }
            if (shader == CharacterShader.Skin) code.AppendLine("baseColor *= saturate(orb.b);");
        }
        foreach (var (output, expression, dimension) in new[] {
                     ("BaseColor","baseColor",3), ("Normal","normal",3), ("Roughness","roughness",1),
                     ("Metallic","metallic",1), ("EmissiveColor","emission",3), ("Opacity","opacity",1) })
            g.Outputs[output] = g.Custom(shader + " " + output, code + "\nreturn " + expression +
                (shader == CharacterShader.Hair && output == "Normal" ? "*Parameters.TwoSidedSign" : "") + ";", dimension, inputs.ToArray());
        if (shader == CharacterShader.Hair)
            return new(shader,g,"MASK",null,Adaptation:"SHPK normal reconstruction and alpha coverage; UE dithered masked coverage approximates the game's solid/edge passes. Color and roughness retain the existing Blender mapping. Game screen-door fades and lighting are not reproduced.",DitherOpacityMask:transparent);
        return new(shader, g, transparent ? "BLEND" : shader == CharacterShader.Iris ? "OPAQUE" : "MASK", null);
    }

    private static string TableCode(MaterialColorTable table)
    {
        var rows = table.Rows;
        var b = new StringBuilder("int pair=min(15,max(0,(int)floor(idx.r*16))); float mixValue=saturate(idx.g); int chosen=2*pair+(idx.g>=0.5 ? 0 : 1);\n");
        string Vec(Vector3 c) => $"float3({F(c.X)},{F(c.Y)},{F(c.Z)})";
        b.AppendLine("float3 Diffuse[32]={" + string.Join(",",rows.Select(r=>Vec(r.Diffuse))) + "};");
        b.AppendLine("float3 Emissive[32]={" + string.Join(",",rows.Select(r=>Vec(r.Emissive))) + "};");
        b.AppendLine("float4 parms[32]={" + string.Join(",",rows.Select(r => $"float4({F(r.Metallic)},{F(r.Roughness)},{F(r.TileIndex)},{F(r.TileAlpha)})")) + "};");
        b.AppendLine("float2 scales[32]={" + string.Join(",",rows.Select(r => $"float2({F(r.TileScale.X)},{F(r.TileScale.Y)})")) + "};");
        b.AppendLine("float3 tableColor=lerp(Diffuse[pair*2+1],Diffuse[pair*2],mixValue), tableEmission=lerp(Emissive[pair*2+1],Emissive[pair*2],mixValue); float4 packed=parms[chosen]; float tileIndex=packed.z,tileAlpha=packed.w; float2 tileScale=scales[chosen];");
        return b.ToString();
    }

    // Blender Normal Map returns world space. Both base and detail use the same UV0 tangent
    // basis, so their sum can be expressed in UE tangent space. V reflection negates Y.
    private const string Helpers = """
        struct Formula {
            float div(float a,float b) { return b==0 ? 0 : a/b; }
            float map(float x,float a,float b) { return saturate(div(x-a,b-a)); }
            float alphaDiv(float a,float t) { return a>0 ? a/(t==0 ? 1:t) : 0; }
            float3 reinhard(float3 c) { return float3(div(c.r,1+c.r),div(c.g,1+c.g),div(c.b,1+c.b)); }
            float bw(float3 c) { return dot(c,float3(0.2126,0.7152,0.0722)); }
            float3 normalFix(float4 n) { return normalize(float3(n.r*2-1,1-n.g*2,1)); }
        } f;
        """;
}
