using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using Meddle.Unreal;

namespace Meddle.Utils.Export.Ue;

/// <summary>Attribute adapters traced from local SM5 SHPK bytecode; see UE_SHPK_REVERSE.md.</summary>
public static class ShpkMaterial
{
    public const string Reference = "FFXIV SM5 SHPK 2026-11-12; normal-swapc-v2; tools/ShpkProbe";
    private static string F(float x) => x.ToString("R", CultureInfo.InvariantCulture);
    private static float N(JsonNode? n, float fallback = 0) => n == null ? fallback : n.GetValue<float>();
    private static float Param(JsonObject d, string key, float fallback = 0) => N(d[key] is JsonArray a ? a.FirstOrDefault() : d[key], fallback);
    private static string Color(JsonObject d, string key, float fallback = 1)
        => "float3(" + string.Join(",", Enumerable.Range(0,3).Select(i=>F(N((d[key] as JsonArray)?.ElementAtOrDefault(i), fallback)))) + ")";
    private static string Key(JsonObject d, string key, string fallback) => d[key]?.GetValue<string>() ?? fallback;

    // These SHPK vertex shaders square RGB before interpolation. Bake that operation at
    // mesh export, not in the pixel shader (which would square interpolated values).
    public static Vector4 VertexColor(Vector4 value) => new(value.X*value.X, value.Y*value.Y, value.Z*value.Z, value.W);

    public static MaterialFormulaResult Build(CharacterShader shader, JsonObject data, IReadOnlyDictionary<string,MaterialTexture> textures)
    {
        var transparent = data["IsTransparent"]?.GetValue<bool>() == true;
        MaterialFormulaResult Gap(string text) => new(shader,null,transparent ? "BLEND" : "OPAQUE",text);
        var compatibility = Key(data,"GetValues","GetValuesMultiMaterial") == "GetValuesCompatibility";
        var vertexMode = Key(data,"CategoryVertexColorMode","ValVertexColorOff");
        var vertexChannels = vertexMode == "ValVertexColorEmissive";
        var decalMode = Key(data,"GetDecalColor","GetDecalColorOff");
        var decal = shader != CharacterShader.Occlusion && decalMode != "GetDecalColorOff";
        if (shader == CharacterShader.Legacy && Key(data,"GetValues","GetValuesMultiMaterial") is not ("GetValuesMultiMaterial" or "GetValuesCompatibility"))
            return Gap("Unverified legacy GetValues branch.");
        if (shader == CharacterShader.Legacy && Key(data,"CategorySpecularType","Default") != "Default")
            return Gap("Unverified legacy specular branch.");
        if (shader == CharacterShader.Legacy && vertexMode is not ("ValVertexColorOff" or "ValVertexColorEmissive"))
            return Gap("Unverified legacy vertex color branch.");
        if (decal && decalMode is not ("GetDecalColorRGBA" or "GetDecalColorAlpha")) return Gap("Unverified decal branch.");
        var required = new List<string> { "g_SamplerNormal" };
        if (shader == CharacterShader.Legacy)
        {
            required.AddRange(["g_SamplerMask","g_SamplerIndex","chara_tile_norm_array","chara_tile_orb_array"]);
            if (compatibility) required.Add("g_SamplerDiffuse");
        }
        if (decal) required.Add("Decal");
        var missing = required.Where(k=>!textures.ContainsKey(k)).ToArray();
        if (missing.Length>0) return Gap("Missing required resources: " + string.Join(", ",missing));

        var g=new MaterialGraph(); var uv=g.Uv(0);
        var inputs=new List<(string Name,MaterialLink Link)> { ("n",g.Sample(textures["g_SamplerNormal"].Package,false,uv)),("vertex",g.VertexColor()) };
        void Add(string name,string usage,MaterialLink coords,bool? srgb=null) => inputs.Add((name,g.Sample(textures[usage].Package,srgb ?? textures[usage].Srgb,coords)));
        var code=new StringBuilder(Helpers);
        code.AppendLine("float3 baseColor=0, emission=0, normal=float3(0,0,1); float roughness=1, specular=0, opacity=1;");
        if(shader==CharacterShader.Occlusion)
        {
            // PS0: o2.rgb=1+(vertex.g-1)*n.a*vertex.a; o1.r uses vertex.b.
            // UE surface materials cannot multiply two separate existing GBuffer targets.
            code.AppendLine("float a=n.a*vertex.a; float factor=a<0.001 ? 1 : lerp(1,vertex.g,a); baseColor=factor.xxx; emission=baseColor;");
        }
        else if(shader==CharacterShader.Tattoo)
        {
            code.AppendLine($"baseColor=lerp(vertex.rgb,{Color(data,"OptionColor",0)},n.b);");
            code.AppendLine("opacity=n.a*vertex.a; opacity=opacity<0.001 ? 0 : opacity;");
            if(decal)
            {
                var uv1=g.Uv(1);
                var duv=g.Custom("SHPK face paint UV",$"return float2(uv.x*{F(Param(data,"FacePaintUVMultiplier"))}+{F(Param(data,"FacePaintUVOffset"))},uv.y);",2,("uv",uv1));
                Add("decal","Decal",duv,false);
                code.AppendLine($"float weight=decal.r*{F(N((data["DecalColor"] as JsonArray)?.ElementAtOrDefault(3)))}*(0.7+0.3*saturate(dot({Color(data,"OptionColor",0)},float3(0.29891,0.58661,0.11448))));");
                code.AppendLine($"baseColor=lerp(baseColor,{Color(data,"DecalColor",0)},weight);");
            }
            // sqrt at the SHPK output encodes the game GBuffer. UE expects linear albedo.
            code.AppendLine($"baseColor*={Color(data,"g_DiffuseColor")}*{Color(data,"g_DiffuseColor")};");
        }
        else
        {
            var rows=data["ColorTable"]?["ColorTable"]?["Rows"] as JsonArray;
            if(rows?.Count!=32) return Gap("SHPK legacy adapter requires the current 32-row color table; 16-row source conversion is not verified.");
            string table;
            try { table=Table(rows); }
            catch (Exception e) when(e is FormatException or InvalidOperationException or NullReferenceException)
            { return Gap("Incomplete structured legacy table: " + e.Message); }
            var index=g.Sample(textures["g_SamplerIndex"].Package,false,uv);
            inputs.Add(("idx",index)); Add("m","g_SamplerMask",uv,false);
            if(compatibility) Add("d","g_SamplerDiffuse",uv,false); // SHPK squares encoded diffuse, not sRGB decoding.
            code.Append(table);
            if(decal)
            {
                Add("decal","Decal",g.Uv(1),false);
                code.AppendLine($"float decalWeight={(decalMode=="GetDecalColorRGBA" ? "decal.a" : "decal.g")}*{F(N((data["DecalColor"] as JsonArray)?.ElementAtOrDefault(3),1))};");
            }
            else code.AppendLine("float decalWeight=0;");
            for(var row=0;row<2;row++)
            {
                var tile=g.Custom("SHPK legacy tile " + row, table + $"\nfloat4 xf=matrix[pair*2+{row}]; float2 t=float2(dot(xf.xy,uv),dot(xf.zw,uv)); return float2(frac(t.x),(clamp(tiles[pair*2+{row}].x,0,63)+frac(t.y))/64);",2,("idx",index),("uv",uv));
                Add("tn"+row,"chara_tile_norm_array",tile,false); Add("orb"+row,"chara_tile_orb_array",tile,false);
            }
            code.AppendLine("float strength0=tiles[pair*2].y*(1-decalWeight), strength1=tiles[pair*2+1].y*(1-decalWeight); float3 detail=normalize(lerp(f.detail(tn1.rg,strength1),f.detail(tn0.rg,strength0),mixValue));");
            // SM5 swapc exchanges its sources when the condition is nonzero.
            code.AppendLine("float3 b=f.detail(n.rg,1); normal=normalize((1-abs(b))*(detail-sign(b))+sign(b)); normal.y=-normal.y;");
            code.AppendLine("float4 orb=lerp(lerp(1,orb1,saturate(strength1)),lerp(1,orb0,saturate(strength0)),mixValue);");
            code.AppendLine($"baseColor=tableDiffuse.rgb*{(compatibility ? "d.rgb*d.rgb" : "m.b*m.b") }*{(vertexChannels ? "vertex.g" : "vertex.rgb")}*orb.b;");
            code.AppendLine("roughness=saturate(exp2(-tableDiffuse.a/15)); emission=tableEmission;");
            // UE Default Lit exposes scalar F0. Preserve colored specular in the table,
            // use luminance/0.08 for its scalar adapter rather than inventing metallic.
            code.AppendLine($"specular=saturate(dot(tableSpecular.rgb,float3(0.2126,0.7152,0.0722))*tableSpecular.a*{(compatibility ? "1" : "m.r*m.r*m.g*m.g")}*{(vertexChannels ? "vertex.b" : "1")}*orb.r*orb.a/0.08);");
            var threshold=F(Param(data,"g_AlphaThreshold",.5f));
            code.AppendLine($"opacity=n.b*vertex.a; opacity={threshold}>0 ? saturate(opacity/{threshold}) : opacity;");
            if(!transparent) code.AppendLine($"opacity=n.b*vertex.a>={threshold} ? 1 : 0;");
            if(decal)
            {
                code.AppendLine(decalMode=="GetDecalColorRGBA"
                    ? $"baseColor=lerp(baseColor,decal.rgb*decal.rgb*{Color(data,"DecalColor")},decalWeight);"
                    : $"baseColor=lerp(baseColor,{Color(data,"DecalColor")},decalWeight);");
            }
        }
        foreach(var (name,expr,dim) in new[]{("BaseColor","baseColor",3),("Normal","normal",3),("Roughness","roughness",1),("Specular","specular",1),("EmissiveColor","emission",3),("Opacity","opacity",1)})
            g.Outputs[name]=g.Custom("SHPK " + shader + " " + name,code+"\nreturn "+expr+";",dim,inputs.ToArray());
        var mode=shader==CharacterShader.Occlusion ? "MODULATE" : shader==CharacterShader.Tattoo || transparent ? "BLEND" : "MASK";
        return new(shader,g,mode,null,shader==CharacterShader.Occlusion,
            shader==CharacterShader.Occlusion ? "UE Unlit Modulate approximates GBuffer albedo occlusion; separate specular occlusion is not reproduced."
            : shader==CharacterShader.Tattoo ? "Linear tattoo color and alpha adapted to a UE translucent surface; game GBuffer overlay lighting differs."
            : "Current 32-row legacy attributes; colored specular is reduced to UE scalar F0. Game lighting, sphere reflection and runtime effects are not reproduced.");
    }

    private static string Table(JsonArray rows)
    {
        float Read(JsonNode? n) { if(n==null) throw new FormatException("Missing value"); var f=n.GetValue<float>(); if(!float.IsFinite(f))throw new FormatException("Nonfinite value");return f; }
        string V(JsonNode? n)=>string.Join(",",new[]{"X","Y","Z"}.Select(k=>F(Read(n?[k]))));
        var s=new StringBuilder("int pair=clamp((int)((idx.r*255+8)/17),0,15); float mixValue=saturate(idx.g);\n");
        s.AppendLine("float4 diffuse[32]={"+string.Join(",",rows.Select(r=>$"float4({V(r?["Diffuse"])},{F(Read(r?["GlossStrength"]))})"))+"};");
        s.AppendLine("float4 spec[32]={"+string.Join(",",rows.Select(r=>$"float4({V(r?["Specular"])},{F(Read(r?["SpecularStrength"]))})"))+"};");
        s.AppendLine("float3 emissive[32]={"+string.Join(",",rows.Select(r=>$"float3({V(r?["Emissive"])})"))+"};");
        s.AppendLine("float2 tiles[32]={"+string.Join(",",rows.Select(r=>$"float2({F(Read(r?["TileIndex"]))},{F(Read(r?["TileAlpha"]))})"))+"};");
        s.AppendLine("float4 matrix[32]={"+string.Join(",",rows.Select(r=>"float4("+string.Join(",",new[]{"UU","UV","VU","VV"}.Select(k=>F(Read(r?["TileMatrix"]?[k]))))+")"))+"};");
        s.AppendLine("float4 tableDiffuse=lerp(diffuse[pair*2+1],diffuse[pair*2],mixValue), tableSpecular=lerp(spec[pair*2+1],spec[pair*2],mixValue); float3 tableEmission=lerp(emissive[pair*2+1],emissive[pair*2],mixValue);");
        return s.ToString();
    }
    private const string Helpers="""
        struct ShpkFormula {
            float3 detail(float2 rg,float strength) { float2 p=rg-0.5; return normalize(float3(p*strength,sqrt(max(0.25-dot(p,p),0.000001)))); }
        } f;
        """;
}
