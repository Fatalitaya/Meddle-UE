using System.Numerics;
using UAssetAPI;

namespace Meddle.Unreal;

public sealed record AssetMaterial(Vector4 BaseColor, float Metallic, float Roughness,
    string? BaseColorTexture = null, string? NormalTexture = null, string? MetallicRoughnessTexture = null,
    string? EmissiveTexture = null, Vector3 Emissive = default, bool TwoSided = false, string AlphaMode = "OPAQUE", float AlphaCutoff = 0.5f);

public static class MaterialWriter
{
    public static void Write(string file, string packagePath, AssetMaterial material)
    {
        var p = new AssetPackage(packagePath);
        p.Asset.CustomVersionContainer.Add(new CustomVersion("FFortniteReleaseBranchCustomObjectVersion", 20));
        var textures = new[] { material.BaseColorTexture, material.NormalTexture, material.MetallicRoughnessTexture, material.EmissiveTexture }
            .Where(x => x != null).Distinct().Cast<string>().ToArray();
        var expressionIndices = textures.Select((path, i) => (path, index: i + 3)).ToDictionary(x => x.path, x => x.index);
        int Expr(string? path) => path == null ? 0 : expressionIndices[path];
        p.AddObject(p.Name, "Material", w =>
        {
            p.ObjectProperty(w, "EditorOnlyData", 2);
            p.BoolProperty(w, "TwoSided", material.TwoSided);
            p.BoolProperty(w, "bUsedWithSkeletalMesh", true);
            p.EnumProperty(w, "BlendMode", "EBlendMode", material.AlphaMode switch { "MASK" => "BLEND_Masked", "BLEND" => "BLEND_Translucent", _ => "BLEND_Opaque" });
            p.Property(w, "OpacityMaskClipValue", p.Bytes(b => b.Write(material.AlphaCutoff)), ("FloatProperty", 0));
            foreach (var name in new[] { "StateId", "LightingGuid" })
                p.NativeProperty(w, name, p.Bytes(b => b.Write(Guid.NewGuid())), ("StructProperty", 1), ("Guid", 1), ("/Script/CoreUObject", 0));
        }, new byte[12]);
        p.AddObject(p.Name + "EditorOnlyData", "MaterialEditorOnlyData", w =>
        {
            Input(p, w, "BaseColor", "ColorMaterialInput", Expr(material.BaseColorTexture), 7, [material.BaseColor.X, material.BaseColor.Y, material.BaseColor.Z, material.BaseColor.W]);
            Input(p, w, "Metallic", "ScalarMaterialInput", Expr(material.MetallicRoughnessTexture), 4, [material.Metallic]);
            Input(p, w, "Roughness", "ScalarMaterialInput", Expr(material.MetallicRoughnessTexture), 2, [material.Roughness]);
            Input(p, w, "Normal", "VectorMaterialInput", Expr(material.NormalTexture), 7, [0, 0, 1]);
            Input(p, w, "EmissiveColor", "ColorMaterialInput", Expr(material.EmissiveTexture), 7, [material.Emissive.X, material.Emissive.Y, material.Emissive.Z, 1]);
            if (material.AlphaMode != "OPAQUE")
                Input(p, w, material.AlphaMode == "MASK" ? "OpacityMask" : "Opacity", "ScalarMaterialInput", Expr(material.BaseColorTexture), 8, [material.BaseColor.W]);
            p.Property(w, "ExpressionCollection", p.Bytes(b =>
            {
                p.Property(b, "Expressions", p.Bytes(v => { v.Write(textures.Length); for (var i = 0; i < textures.Length; i++) v.Write(i + 3); }), ("ArrayProperty", 1), ("ObjectProperty", 0));
                p.End(b);
            }), ("StructProperty", 1), ("MaterialExpressionCollection", 1), ("/Script/Engine", 0));
        }, new byte[4], outer: 1);
        foreach (var path in textures)
        {
            p.AddObject("Sample_" + expressionIndices[path], "MaterialExpressionTextureSample", w =>
            {
                p.ObjectProperty(w, "Material", 1);
                p.ObjectProperty(w, "Texture", p.Reference(path, "Texture2D"));
                p.EnumProperty(w, "SamplerType", "EMaterialSamplerType", path == material.NormalTexture ? "SAMPLERTYPE_Normal" : path == material.MetallicRoughnessTexture ? "SAMPLERTYPE_LinearColor" : "SAMPLERTYPE_Color");
                p.NativeProperty(w, "MaterialExpressionGuid", p.Bytes(b => b.Write(Guid.NewGuid())), ("StructProperty", 1), ("Guid", 1), ("/Script/CoreUObject", 0));
            }, [], outer: 1);
        }
        p.Save(file);
    }

    private static void Input(AssetPackage p, AssetBinaryWriter w, string name, string type, int expression, int mask, float[] constant)
    {
        p.NativeProperty(w, name, p.Bytes(b =>
        {
            b.Write(expression); b.Write(0); b.Write(p.NameOf("None"));
            b.Write(1); for (var bit = 0; bit < 4; bit++) b.Write((mask >> bit) & 1);
            b.Write(expression == 0 ? 1 : 0);
            foreach (var v in constant) b.Write(v);
        }), ("StructProperty", 1), (type, 1), ("/Script/Engine", 0));
    }
}
