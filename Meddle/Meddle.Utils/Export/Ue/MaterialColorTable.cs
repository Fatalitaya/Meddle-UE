using System.Globalization;
using System.Numerics;
using System.Text.Json.Nodes;

namespace Meddle.Utils.Export.Ue;

/// <summary>Decoded properties, never the raw ushort bit fields or the preview PNG.</summary>
public sealed record MaterialColorRow(Vector3 Diffuse, Vector3 Emissive, float Metallic, float Roughness,
    float TileIndex, float TileAlpha, Vector2 TileScale);

public sealed record MaterialColorTable(MaterialColorRow[] Rows)
{
    public static MaterialColorTable? Read(JsonNode? node)
    {
        if (node is not JsonArray { Count: 32 } rows) return null;
        var result = new List<MaterialColorRow>();
        float Read(JsonNode? value)
        {
            if (value == null || !float.TryParse(value.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var f) || !float.IsFinite(f))
                throw new FormatException("Missing or nonfinite structured color table value.");
            return f;
        }
        Vector3 Color(JsonNode? value) => new(Read(value?["X"]),Read(value?["Y"]),Read(value?["Z"]));
        try
        {
            foreach (var row in rows)
                result.Add(new(Color(row?["Diffuse"]),Color(row?["Emissive"]),Read(row?["Metalness"]),Read(row?["Roughness"]),
                    Read(row?["TileIndex"]),Read(row?["TileAlpha"]),new(Read(row?["TileMatrix"]?["UU"]),Read(row?["TileMatrix"]?["VV"]))));
        }
        catch (FormatException) { return null; }
        return new(result.ToArray());
    }

    /// <summary>Four RGBA32F texels per row, for editable source preservation; ramps use constants.</summary>
    public float[] ToFloatPixels() => Rows.SelectMany(r => new[] {
        r.Diffuse.X,r.Diffuse.Y,r.Diffuse.Z,1,
        r.Emissive.X,r.Emissive.Y,r.Emissive.Z,1,
        r.Metallic,r.Roughness,r.TileIndex,r.TileAlpha,
        r.TileScale.X,r.TileScale.Y,0,0
    }).ToArray();
}
