using System.Text.Json.Serialization;
using Meddle.Utils.Files.Structs.Material;

namespace Meddle.Utils.Export.Ue;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(UeModernColorTableSetSnapshot), "Modern")]
[JsonDerivedType(typeof(UeLegacyColorTableSetSnapshot), "Legacy")]
public abstract class UeColorTableSetSnapshot
{
}

public sealed class UeModernColorTableSetSnapshot : UeColorTableSetSnapshot
{
    public List<UeColorTableRowSnapshot> ColorTable { get; set; } = [];
    public List<UeColorDyeRowSnapshot>? ColorDyeTable { get; set; }
}

public sealed class UeLegacyColorTableSetSnapshot : UeColorTableSetSnapshot
{
    public List<UeLegacyColorTableRowSnapshot> ColorTable { get; set; } = [];
    public List<UeLegacyColorDyeRowSnapshot>? ColorDyeTable { get; set; }
}

public sealed class UeColorTableRowSnapshot
{
    public float[] Diffuse { get; set; } = [];
    public float GlossStrength { get; set; }
    public float Scalar3 { get; set; }
    public float[] Specular { get; set; } = [];
    public float SpecularStrength { get; set; }
    public float Scalar7 { get; set; }
    public float[] Emissive { get; set; } = [];
    public float Scalar11 { get; set; }
    public float SheenRate { get; set; }
    public float SheenTint { get; set; }
    public float SheenAptitude { get; set; }
    public float Scalar15 { get; set; }
    public float Roughness { get; set; }
    public float Scalar17 { get; set; }
    public float Metalness { get; set; }
    public float Anisotropy { get; set; }
    public float Scalar20 { get; set; }
    public float SphereMask { get; set; }
    public float Scalar22 { get; set; }
    public float Scalar23 { get; set; }
    public ushort ShaderId { get; set; }
    public byte TileIndex { get; set; }
    public float TileAlpha { get; set; }
    public ushort SphereIndex { get; set; }
    public UeColorTableMatrixSnapshot TileMatrix { get; set; } = new();
}

public sealed class UeLegacyColorTableRowSnapshot
{
    public float[] Diffuse { get; set; } = [];
    public float[] Specular { get; set; } = [];
    public float SpecularStrength { get; set; }
    public float[] Emissive { get; set; } = [];
    public float GlossStrength { get; set; }
    public float[] MaterialRepeat { get; set; } = [];
    public float[] MaterialSkew { get; set; } = [];
    public ushort TileIndex { get; set; }
}

public sealed class UeColorTableMatrixSnapshot
{
    public float UU { get; set; }
    public float UV { get; set; }
    public float VU { get; set; }
    public float VV { get; set; }
}

public sealed class UeColorDyeRowSnapshot
{
    public ushort Template { get; set; }
    public byte Channel { get; set; }
    public bool Diffuse { get; set; }
    public bool Specular { get; set; }
    public bool Emissive { get; set; }
    public bool Scalar3 { get; set; }
    public bool Metalness { get; set; }
    public bool Roughness { get; set; }
    public bool SheenRate { get; set; }
    public bool SheenTintRate { get; set; }
    public bool SheenAperture { get; set; }
    public bool Anisotropy { get; set; }
    public bool SphereMapIndex { get; set; }
    public bool SphereMapMask { get; set; }
}

public sealed class UeLegacyColorDyeRowSnapshot
{
    public ushort Template { get; set; }
    public bool Diffuse { get; set; }
    public bool Specular { get; set; }
    public bool Emissive { get; set; }
    public bool Gloss { get; set; }
    public bool SpecularStrength { get; set; }
}

public static class UeColorTableSnapshotMapper
{
    public static UeColorTableSetSnapshot? Map(IColorTableSet? colorTable)
    {
        return colorTable switch
        {
            ColorTableSet modern => MapModern(modern),
            LegacyColorTableSet legacy => MapLegacy(legacy),
            _ => null
        };
    }

    private static UeModernColorTableSetSnapshot MapModern(ColorTableSet set)
    {
        var snapshot = new UeModernColorTableSetSnapshot();

        foreach (var row in set.ColorTable.Rows.ToArray())
        {
            snapshot.ColorTable.Add(new UeColorTableRowSnapshot
            {
                Diffuse = ToArray(row.Diffuse),
                GlossStrength = row.GlossStrength,
                Scalar3 = row.Scalar3,
                Specular = ToArray(row.Specular),
                SpecularStrength = row.SpecularStrength,
                Scalar7 = row.Scalar7,
                Emissive = ToArray(row.Emissive),
                Scalar11 = row.Scalar11,
                SheenRate = row.SheenRate,
                SheenTint = row.SheenTint,
                SheenAptitude = row.SheenAptitude,
                Scalar15 = row.Scalar15,
                Roughness = row.Roughness,
                Scalar17 = row.Scalar17,
                Metalness = row.Metalness,
                Anisotropy = row.Anisotropy,
                Scalar20 = row.Scalar20,
                SphereMask = row.SphereMask,
                Scalar22 = row.Scalar22,
                Scalar23 = row.Scalar23,
                ShaderId = row.ShaderId,
                TileIndex = row.TileIndex,
                TileAlpha = row.TileAlpha,
                SphereIndex = row.SphereIndex,
                TileMatrix = new UeColorTableMatrixSnapshot
                {
                    UU = row.TileMatrix.UU,
                    UV = row.TileMatrix.UV,
                    VU = row.TileMatrix.VU,
                    VV = row.TileMatrix.VV
                }
            });
        }

        if (set.ColorDyeTable is { } dyeTable)
        {
            snapshot.ColorDyeTable = [];
            foreach (var row in dyeTable.Rows.ToArray())
            {
                snapshot.ColorDyeTable.Add(new UeColorDyeRowSnapshot
                {
                    Template = row.Template,
                    Channel = row.Channel,
                    Diffuse = row.DiffuseColor,
                    Specular = row.SpecularColor,
                    Emissive = row.EmissiveColor,
                    Scalar3 = row.Scalar3,
                    Metalness = row.Metalness,
                    Roughness = row.Roughness,
                    SheenRate = row.SheenRate,
                    SheenTintRate = row.SheenTintRate,
                    SheenAperture = row.SheenAperture,
                    Anisotropy = row.Anisotropy,
                    SphereMapIndex = row.SphereMapIndex,
                    SphereMapMask = row.SphereMapMask
                });
            }
        }

        return snapshot;
    }

    private static UeLegacyColorTableSetSnapshot MapLegacy(LegacyColorTableSet set)
    {
        var snapshot = new UeLegacyColorTableSetSnapshot();

        foreach (var row in set.ColorTable.Rows.ToArray())
        {
            snapshot.ColorTable.Add(new UeLegacyColorTableRowSnapshot
            {
                Diffuse = ToArray(row.Diffuse),
                Specular = ToArray(row.Specular),
                SpecularStrength = row.SpecularStrength,
                Emissive = ToArray(row.Emissive),
                GlossStrength = row.GlossStrength,
                MaterialRepeat = [row.MaterialRepeat.X, row.MaterialRepeat.Y],
                MaterialSkew = [row.MaterialSkew.X, row.MaterialSkew.Y],
                TileIndex = row.TileIndex
            });
        }

        if (set.ColorDyeTable is { } dyeTable)
        {
            snapshot.ColorDyeTable = [];
            foreach (var row in dyeTable.Rows.ToArray())
            {
                snapshot.ColorDyeTable.Add(new UeLegacyColorDyeRowSnapshot
                {
                    Template = row.Template,
                    Diffuse = row.DiffuseColor,
                    Specular = row.SpecularColor,
                    Emissive = row.EmissiveColor,
                    Gloss = row.Shininess,
                    SpecularStrength = row.SpecularMask
                });
            }
        }

        return snapshot;
    }

    private static float[] ToArray(System.Numerics.Vector3 value)
    {
        return [value.X, value.Y, value.Z];
    }
}
