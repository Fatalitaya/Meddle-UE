using System.Numerics;
using UAssetAPI;

namespace Meddle.Unreal;

public sealed record AssetMaterial(Vector4 BaseColor, float Metallic, float Roughness,
    string? BaseColorTexture = null, string? NormalTexture = null, string? MetallicRoughnessTexture = null,
    string? EmissiveTexture = null, Vector3 Emissive = default, bool TwoSided = false, string AlphaMode = "OPAQUE", float AlphaCutoff = 0.5f, MaterialGraph? Graph = null, bool PackedNormal = false, bool Unlit = false, bool DitherOpacityMask = false);

public static class MaterialWriter
{
    public static void Write(string file, string packagePath, AssetMaterial material)
    {
        var p = new AssetPackage(packagePath);
        p.Asset.CustomVersionContainer.Add(new CustomVersion("FFortniteReleaseBranchCustomObjectVersion", 20));
        var graph = material.Graph ?? BasicGraph(material);
        MaterialLink Output(string name) => graph.Outputs.GetValueOrDefault(name);
        p.AddObject(p.Name, "Material", w =>
        {
            p.ObjectProperty(w, "EditorOnlyData", 2);
            p.BoolProperty(w, "TwoSided", material.TwoSided);
            p.BoolProperty(w, "DitherOpacityMask", material.DitherOpacityMask);
            if (material.Unlit) p.EnumProperty(w, "ShadingModel", "EMaterialShadingModel", "MSM_Unlit");
            p.BoolProperty(w, "bUsedWithSkeletalMesh", true);
            p.EnumProperty(w, "BlendMode", "EBlendMode", material.AlphaMode switch { "MASK" => "BLEND_Masked", "BLEND" => "BLEND_Translucent", "MODULATE" => "BLEND_Modulate", _ => "BLEND_Opaque" });
            if (material.AlphaMode == "MODULATE") p.EnumProperty(w, "TranslucencyPass", "EMaterialTranslucencyPass", "MTP_BeforeDOF");
            p.Property(w, "OpacityMaskClipValue", p.Bytes(b => b.Write(material.AlphaCutoff)), ("FloatProperty", 0));
            foreach (var name in new[] { "StateId", "LightingGuid" })
                p.NativeProperty(w, name, p.Bytes(b => b.Write(Guid.NewGuid())), ("StructProperty", 1), ("Guid", 1), ("/Script/CoreUObject", 0));
        }, new byte[12]);
        p.AddObject(p.Name + "EditorOnlyData", "MaterialEditorOnlyData", w =>
        {
            Input(p, w, "BaseColor", "ColorMaterialInput", Output("BaseColor"), [material.BaseColor.X, material.BaseColor.Y, material.BaseColor.Z, material.BaseColor.W]);
            Input(p, w, "Metallic", "ScalarMaterialInput", Output("Metallic"), [material.Metallic]);
            Input(p, w, "Roughness", "ScalarMaterialInput", Output("Roughness"), [material.Roughness]);
            if (graph.Outputs.ContainsKey("Specular")) Input(p, w, "Specular", "ScalarMaterialInput", Output("Specular"), [0.5f]);
            Input(p, w, "Normal", "VectorMaterialInput", Output("Normal"), [0, 0, 1]);
            Input(p, w, "EmissiveColor", "ColorMaterialInput", Output("EmissiveColor"), [material.Emissive.X, material.Emissive.Y, material.Emissive.Z, 1]);
            if (material.AlphaMode != "OPAQUE")
                Input(p, w, material.AlphaMode == "MASK" ? "OpacityMask" : "Opacity", "ScalarMaterialInput", Output("Opacity"), [material.BaseColor.W]);
            p.Property(w, "ExpressionCollection", p.Bytes(b =>
            {
                p.Property(b, "Expressions", p.Bytes(v => { v.Write(graph.Nodes.Count); for (var i = 0; i < graph.Nodes.Count; i++) v.Write(i + 3); }), ("ArrayProperty", 1), ("ObjectProperty", 0));
                p.End(b);
            }), ("StructProperty", 1), ("MaterialExpressionCollection", 1), ("/Script/Engine", 0));
        }, new byte[4], outer: 1);
        for (var i = 0; i < graph.Nodes.Count; i++)
        {
            var node = graph.Nodes[i];
            var type = node switch
            {
                MaterialUv => "MaterialExpressionTextureCoordinate",
                MaterialVertexColor => "MaterialExpressionVertexColor",
                MaterialSample => "MaterialExpressionTextureSample",
                MaterialCustom => "MaterialExpressionCustom",
                _ => throw new NotSupportedException()
            };
            p.AddObject("Expression_" + (i + 3), type, w =>
            {
                p.ObjectProperty(w, "Material", 1);
                p.IntProperty(w, "MaterialExpressionEditorX", -600 + i % 4 * 180);
                p.IntProperty(w, "MaterialExpressionEditorY", i / 4 * 180);
                p.NativeProperty(w, "MaterialExpressionGuid", p.Bytes(b => b.Write(Guid.NewGuid())), ("StructProperty", 1), ("Guid", 1), ("/Script/CoreUObject", 0));
                switch (node)
                {
                    case MaterialUv uv:
                        p.IntProperty(w, "CoordinateIndex", uv.Channel);
                        break;
                    case MaterialSample sample:
                        p.ObjectProperty(w, "Texture", p.Reference(sample.Path, "Texture2D"));
                        p.EnumProperty(w, "SamplerType", "EMaterialSamplerType", sample.Normal ? "SAMPLERTYPE_Normal" : sample.Srgb ? "SAMPLERTYPE_Color" : "SAMPLERTYPE_LinearColor");
                        ExpressionInput(p, w, "Coordinates", sample.Uv);
                        if (sample.MipBias is {} mipBias)
                        {
                            p.EnumProperty(w, "MipValueMode", "ETextureMipValueMode", "TMVM_MipBias");
                            ExpressionInput(p, w, "MipValue", mipBias);
                        }
                        break;
                    case MaterialCustom custom:
                        p.Property(w, "Code", p.Bytes(b => b.Write(new UAssetAPI.UnrealTypes.FString(custom.Code))), ("StrProperty", 0));
                        p.Property(w, "Description", p.Bytes(b => b.Write(new UAssetAPI.UnrealTypes.FString(custom.Label))), ("StrProperty", 0));
                        p.EnumProperty(w, "OutputType", "ECustomMaterialOutputType", "CMOT_Float" + custom.Dimensions);
                        p.Property(w, "Inputs", p.Bytes(b =>
                        {
                            b.Write(custom.Inputs.Length);
                            foreach (var input in custom.Inputs)
                            {
                                p.Property(b, "InputName", p.Bytes(v => v.Write(p.NameOf(input.Name))), ("NameProperty", 0));
                                ExpressionInput(p, b, "Input", input.Link);
                                p.End(b);
                            }
                        }), ("ArrayProperty", 1), ("StructProperty", 1), ("CustomInput", 1), ("/Script/Engine", 0));
                        break;
                }
            }, [], outer: 1);
        }
        p.Save(file);
    }

    private static MaterialGraph BasicGraph(AssetMaterial m)
    {
        var g = new MaterialGraph();
        var uv = g.Uv(0);
        if (m.BaseColorTexture != null)
        {
            var sample = g.Sample(m.BaseColorTexture, true, uv);
            var c = m.BaseColor;
            g.Outputs["BaseColor"] = g.Custom("Linear color factor", FormattableString.Invariant($"return tex.rgb * float3({c.X:R}, {c.Y:R}, {c.Z:R});"), 3, ("tex", sample));
            g.Outputs["Opacity"] = sample with { Mask = 8 };
        }
        if (m.NormalTexture != null)
        {
            var sample = g.Sample(m.NormalTexture, false, uv, !m.PackedNormal);
            g.Outputs["Normal"] = m.PackedNormal
                ? g.Custom("Normal_Fix / UE V reflection", "return normalize(float3(tex.r*2-1,1-tex.g*2,1));", 3, ("tex", sample))
                : sample with { Mask = 7 };
        }
        if (m.MetallicRoughnessTexture != null)
        {
            var sample = g.Sample(m.MetallicRoughnessTexture, false, uv);
            g.Outputs["Metallic"] = sample with { Mask = 4 };
            g.Outputs["Roughness"] = sample with { Mask = 2 };
        }
        if (m.EmissiveTexture != null) g.Outputs["EmissiveColor"] = g.Sample(m.EmissiveTexture, true, uv) with { Mask = 7 };
        return g;
    }

    private static void Link(AssetPackage p, AssetBinaryWriter b, MaterialLink link)
    {
        b.Write(link.Expression); b.Write(link.Output); b.Write(p.NameOf("None"));
        b.Write(link.Mask == 0 ? 0 : 1);
        for (var bit = 0; bit < 4; bit++) b.Write((link.Mask >> bit) & 1);
    }

    private static void ExpressionInput(AssetPackage p, AssetBinaryWriter w, string name, MaterialLink link)
        => p.NativeProperty(w, name, p.Bytes(b => Link(p, b, link)), ("StructProperty", 1), ("ExpressionInput", 1), ("/Script/Engine", 0));

    private static void Input(AssetPackage p, AssetBinaryWriter w, string name, string type, MaterialLink link, float[] constant)
    {
        p.NativeProperty(w, name, p.Bytes(b =>
        {
            Link(p, b, link);
            b.Write(link.Expression == 0 ? 1 : 0);
            foreach (var v in constant) b.Write(v);
        }), ("StructProperty", 1), (type, 1), ("/Script/Engine", 0));
    }
}
