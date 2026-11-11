using System.Buffers.Binary;
using UAssetAPI.ExportTypes;

namespace Meddle.Unreal;

public static class TextureWriter
{
    /// <summary>Writes editable BGRA8 source pixels. UE builds its own platform data on load.</summary>
    public static void Write(string file, string packagePath, int width, int height, byte[] bgra, bool srgb, bool normalMap = false)
    {
        if (width <= 0 || height <= 0 || (long)width * height * 4 != bgra.Length)
            throw new ArgumentException("Texture dimensions do not match BGRA8 data.");
        var p = new AssetPackage(packagePath);
        // Use the supported pre-virtualization texture source encoding. This is an actual
        // custom-version selection, not a claim that virtualized payloads have been written.
        p.Asset.CustomVersionContainer.Single(v => v.FriendlyName == "FUE5MainStreamObjectVersion").Version = 0;
        var guid = Guid.NewGuid();
        p.AddObject(p.Name, "Texture2D", w =>
        {
            p.BoolProperty(w, "SRGB", srgb);
            p.EnumProperty(w, "CompressionSettings", "TextureCompressionSettings", normalMap ? "TC_Normalmap" : "TC_Default");
            p.Property(w, "Source", p.Bytes(b =>
            {
                p.NativeProperty(b, "Id", p.Bytes(v => v.Write(guid)), ("StructProperty", 1), ("Guid", 1), ("/Script/CoreUObject", 0));
                p.IntProperty(b, "SizeX", width); p.IntProperty(b, "SizeY", height);
                p.IntProperty(b, "NumSlices", 1); p.IntProperty(b, "NumMips", 1);
                p.EnumProperty(b, "Format", "ETextureSourceFormat", "TSF_BGRA8");
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
        using (p.Asset.WriteData()) { }
        var export = (RawExport)p.Asset.Exports[0];
        var nativeStart = checked((int)export.ScriptSerializationEndOffset + 4);
        BinaryPrimitives.WriteInt64LittleEndian(export.Data.AsSpan(nativeStart + 14, 8), export.SerialOffset + nativeStart + 22);
        p.Save(file);
    }
}
