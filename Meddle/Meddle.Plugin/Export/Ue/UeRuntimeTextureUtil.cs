using Meddle.Utils;
using SkiaSharp;

namespace Meddle.Plugin.Export.Ue;

public static class UeRuntimeTextureUtil
{
    public static byte[] EncodePng(SkTexture texture)
    {
        using var bitmap = texture.Bitmap;
        using var stream = new MemoryStream();
        if (bitmap == null || !bitmap.Encode(stream, SKEncodedImageFormat.Png, 100) || stream.Length == 0)
        {
            throw new InvalidOperationException("Failed to encode runtime decal texture as PNG.");
        }

        return stream.ToArray();
    }
}
