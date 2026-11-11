using System.Buffers.Binary;
using System.Collections;
using UAssetAPI.ExportTypes;

namespace Meddle.Unreal;

public static class TextureWriter
{
    /// <summary>Writes editable BGRA8 source pixels. UE builds its own platform data on load.</summary>
    public static void Write(string file, string packagePath, int width, int height, byte[] bgra, bool srgb, bool normalMap = false, bool nearest = false, bool clamp = false, bool linearData = false)
    {
        if (width <= 0 || height <= 0 || (long)width * height * 4 != bgra.Length)
            throw new ArgumentException("Texture dimensions do not match BGRA8 data.");
        WriteSource(file, packagePath, width, height, bgra, "TSF_BGRA8", srgb,
            normalMap ? "TC_Normalmap" : linearData ? "TC_VectorDisplacementmap" : "TC_Default", nearest, clamp);
    }

    /// <summary>RGBA32F source preserves table values and HDR. Packed identifiers must be decoded before calling.</summary>
    public static void WriteFloat(string file, string packagePath, int width, int height, float[] rgba, bool nearest = true, bool clamp = true)
    {
        if (width <= 0 || height <= 0 || (long)width * height * 4 != rgba.Length)
            throw new ArgumentException("Texture dimensions do not match RGBA32F data.");
        var bytes = new byte[rgba.Length * 4];
        for (var i = 0; i < rgba.Length; i++) BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(i * 4), rgba[i]);
        WriteSource(file, packagePath, width, height, bytes, "TSF_RGBA32F", false, "TC_HDR_F32", nearest, clamp);
    }

    private static void WriteSource(string file, string packagePath, int width, int height, byte[] bgra,
        string format, bool srgb, string compression, bool nearest, bool clamp)
    {
        var p = new AssetPackage(packagePath);
        // Use the supported pre-virtualization texture source encoding. This is an actual
        // custom-version selection, not a claim that virtualized payloads have been written.
        p.Asset.CustomVersionContainer.Single(v => v.FriendlyName == "FUE5MainStreamObjectVersion").Version = 0;
        var guid = Guid.NewGuid();
        p.AddObject(p.Name, "Texture2D", w =>
        {
            p.BoolProperty(w, "SRGB", srgb);
            p.EnumProperty(w, "CompressionSettings", "TextureCompressionSettings", compression);
            p.EnumProperty(w, "Filter", "TextureFilter", nearest ? "TF_Nearest" : "TF_Bilinear");
            p.EnumProperty(w, "AddressX", "TextureAddress", clamp ? "TA_Clamp" : "TA_Wrap");
            p.EnumProperty(w, "AddressY", "TextureAddress", clamp ? "TA_Clamp" : "TA_Wrap");
            p.EnumProperty(w, "MipGenSettings", "TextureMipGenSettings", "TMGS_NoMipmaps");
            p.Property(w, "Source", p.Bytes(b =>
            {
                p.NativeProperty(b, "Id", p.Bytes(v => v.Write(guid)), ("StructProperty", 1), ("Guid", 1), ("/Script/CoreUObject", 0));
                p.IntProperty(b, "SizeX", width); p.IntProperty(b, "SizeY", height);
                p.IntProperty(b, "NumSlices", 1); p.IntProperty(b, "NumMips", 1);
                p.EnumProperty(b, "Format", "ETextureSourceFormat", format);
                p.End(b);
            }), ("StructProperty", 1), ("TextureSource", 1), ("/Script/Engine", 0));
        }, p.Bytes(w =>
        {
            w.Write((ushort)0); // UTexture strip flags
            w.Write((uint)((1 << 6) | (1 << 16))); // force inline, absolute offset
            w.Write(bgra.Length); w.Write(bgra.Length); w.Write(0L);
            w.Write(bgra);
            w.Write((ushort)0); w.Write(0); // UTexture2D strip flags / not cooked
        }));
        // Inline bulk payload offsets are absolute file offsets, fixed after table layout.
        // Match Save's import bit array before measuring: adding it afterwards moves the
        // export by four bytes, silently shifting every source pixel on UE load.
        p.Asset.ImportBits = new BitArray(p.Asset.Imports.Count, true);
        using (p.Asset.WriteData()) { }
        var export = (RawExport)p.Asset.Exports[0];
        var nativeStart = checked((int)export.ScriptSerializationEndOffset + 4);
        BinaryPrimitives.WriteInt64LittleEndian(export.Data.AsSpan(nativeStart + 14, 8), export.SerialOffset + nativeStart + 22);
        p.Save(file);
    }
}
