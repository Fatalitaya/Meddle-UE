using System.Numerics;
using Meddle.Formats.Files.MtrlFile;
using SkiaSharp;

namespace Meddle.Utils.Helpers;

/// <summary>
/// Presentation adapters for the raw color table data exposed by
/// Meddle.Formats. These conversions depend on SkiaSharp/SkTexture and
/// therefore live in the Utils presentation layer, not in Formats.
/// </summary>
public static class ColorTableImageUtils
{
    /// <summary>
    /// Semantics-identical to the historical ShortVec4.ToSkColor():
    /// half-float -&gt; Vector4 -&gt; component clamp [0,1] -&gt; byte * 255.
    /// </summary>
    public static SKColor ToSkColor(this ShortVec4 value)
    {
        var color = value.ToVector4();
        var c = Clamp(color, 0, 1);
        return new SKColor((byte)(c.X * 255), (byte)(c.Y * 255), (byte)(c.Z * 255), (byte)(c.W * 255));
    }

    public static SkTexture ToTexture(this ColorTable table)
    {
        var texture = new SkTexture(ColorTable.TextureSize.Width, ColorTable.TextureSize.Height);
        for (var x = 0; x < ColorTable.TextureSize.Width; x++)
        {
            for (var y = 0; y < ColorTable.TextureSize.Height; y++)
            {
                texture[x, y] = table.Buffer[y][x].ToSkColor();
            }
        }

        return texture;
    }

    public static SkTexture ToTexture(this LegacyColorTable table)
    {
        var texture = new SkTexture(LegacyColorTable.TextureSize.Width, LegacyColorTable.TextureSize.Height);
        for (var x = 0; x < LegacyColorTable.TextureSize.Width; x++)
        {
            for (var y = 0; y < LegacyColorTable.TextureSize.Height; y++)
            {
                texture[x, y] = table.Buffer[y][x].ToSkColor();
            }
        }

        return texture;
    }

    private static Vector4 Clamp(Vector4 value, float min, float max)
    {
        return new Vector4(
            Math.Clamp(value.X, min, max),
            Math.Clamp(value.Y, min, max),
            Math.Clamp(value.Z, min, max),
            Math.Clamp(value.W, min, max));
    }
}
