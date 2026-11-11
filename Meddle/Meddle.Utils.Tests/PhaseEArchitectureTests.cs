using Meddle.Formats;
using Meddle.Formats.Files;
using Meddle.Formats.Files.MtrlFile;
using Meddle.Utils.Helpers;
using OtterTex;
using SkiaSharp;

namespace Meddle.Utils.Tests;

/// <summary>
/// Phase E regressions proving the mechanical migration did not alter behavior:
/// Crc32 known values, TEX -&gt; DXGI/dimension mapping, and color-table
/// presentation conversion. Expected values are literals, never produced by
/// the implementation under test.
/// </summary>
[TestClass]
public sealed class PhaseEArchitectureTests
{
    [TestMethod]
    public void Crc32_KnownGameConstants_KeepLiteralValues()
    {
        Assert.AreEqual(0xD62BF368u, Crc32.GetHash("g_AlphaAperture"));
        Assert.AreEqual(0x07EDA444u, Crc32.GetHash("g_AlphaMultiParam"));
        Assert.AreEqual(0xB18FE63Du, Crc32.GetHash("Default"));
    }

    [TestMethod]
    public void ToTexDimension_MapsRawAttributes()
    {
        Assert.AreEqual(TexDimension.Tex1D, TexFile.Attribute.TextureType1D.ToTexDimension());
        Assert.AreEqual(TexDimension.Tex2D, TexFile.Attribute.TextureType2D.ToTexDimension());
        Assert.AreEqual(TexDimension.Tex3D, TexFile.Attribute.TextureType3D.ToTexDimension());
        Assert.AreEqual(TexDimension.Tex2D, TexFile.Attribute.TextureType2DArray.ToTexDimension());
        Assert.AreEqual(TexDimension.Tex2D, TexFile.Attribute.TextureTypeCube.ToTexDimension());
    }

    [TestMethod]
    public void ToDxgiFormat_MapsRawFormats()
    {
        Assert.AreEqual(DXGIFormat.R8UNorm, TexFile.TextureFormat.R8_UNORM.ToDxgiFormat());
        Assert.AreEqual(DXGIFormat.B8G8R8A8UNorm, TexFile.TextureFormat.B8G8R8A8_UNORM.ToDxgiFormat());
        Assert.AreEqual(DXGIFormat.R16G16B16A16Float, TexFile.TextureFormat.R16G16B16A16F.ToDxgiFormat());
        Assert.AreEqual(DXGIFormat.BC1UNorm, TexFile.TextureFormat.BC1_UNORM.ToDxgiFormat());
        Assert.AreEqual(DXGIFormat.BC3UNorm, TexFile.TextureFormat.BC3_UNORM.ToDxgiFormat());
        Assert.AreEqual(DXGIFormat.BC5UNorm, TexFile.TextureFormat.BC5_UNORM.ToDxgiFormat());
        Assert.AreEqual(DXGIFormat.BC7UNorm, TexFile.TextureFormat.BC7_UNORM.ToDxgiFormat());
        Assert.AreEqual(DXGIFormat.R10G10B10A2UNorm, TexFile.TextureFormat.R10G10B10A2_UNORM.ToDxgiFormat());
    }

    [TestMethod]
    public void ShortVec4_ToSkColor_ClampsAndOrdersChannels()
    {
        var value = new ShortVec4
        {
            X = ShortVec4.FromFloat(1.0f),
            Y = ShortVec4.FromFloat(0.5f),
            Z = ShortVec4.FromFloat(0.25f),
            W = ShortVec4.FromFloat(1.0f)
        };

        var color = value.ToSkColor();

        Assert.AreEqual((byte)255, color.Red);
        Assert.AreEqual((byte)127, color.Green);
        Assert.AreEqual((byte)63, color.Blue);
        Assert.AreEqual((byte)255, color.Alpha);

        var clamped = new ShortVec4
        {
            X = ShortVec4.FromFloat(2.0f),
            Y = ShortVec4.FromFloat(-1.0f),
            Z = ShortVec4.FromFloat(0f),
            W = ShortVec4.FromFloat(0f)
        }.ToSkColor();

        Assert.AreEqual((byte)255, clamped.Red);
        Assert.AreEqual((byte)0, clamped.Green);
        Assert.AreEqual((byte)0, clamped.Blue);
    }

    [TestMethod]
    public void ColorTable_DimensionsAndTextureSize()
    {
        Assert.AreEqual((8, 32), ColorTable.TextureSize);
        Assert.AreEqual((4, 16), LegacyColorTable.TextureSize);
    }

    [TestMethod]
    public void ColorTable_ToTexture_RebuildsPixelBuffer()
    {
        var data = new byte[ColorTable.Size];
        var reader = new SpanBinaryReader(data);
        var table = new ColorTable(ref reader);

        var texture = table.ToTexture();

        Assert.AreEqual(8, texture.Width);
        Assert.AreEqual(32, texture.Height);
        Assert.AreEqual(new SKColor(0, 0, 0, 0), texture[0, 0]);
        Assert.AreEqual(new SKColor(0, 0, 0, 0), texture[7, 31]);
    }
}
