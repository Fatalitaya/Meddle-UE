using System.Runtime.InteropServices;
using System.Text;
using Meddle.Formats;

namespace Meddle.Formats.Files;

public class PapFile
{
    public const uint PapMagic = 0x20706170; // "pap " (little-endian)
    public PapFileHeader FileHeader;
    public PapAnimation[] Animations;
    
    private readonly byte[] data;
    public ReadOnlySpan<byte> RawData => data;
    public ReadOnlySpan<byte> HavokData => RawData.Slice((int)FileHeader.HavokOffset, (int)FileHeader.FooterOffset - (int)FileHeader.HavokOffset);
    public ReadOnlySpan<byte> FooterData => RawData.Slice((int)FileHeader.FooterOffset);
    
    public PapFile(byte[] data) : this((ReadOnlySpan<byte>)data) { }

    public PapFile(ReadOnlySpan<byte> data)
    {
        this.data = data.ToArray();
        var reader = new SpanBinaryReader(data);
        FileHeader = reader.Read<PapFileHeader>();
        if (FileHeader.Magic != PapMagic)
            throw new InvalidDataException($"Invalid PAP magic: expected 0x{PapMagic:X8}, actual 0x{FileHeader.Magic:X8}");
        Animations = reader.Read<PapAnimation>(FileHeader.AnimationCount).ToArray();
        ValidateOffsets(data.Length);
    }

    private void ValidateOffsets(int dataLength)
    {
        if (FileHeader.HavokOffset > FileHeader.FooterOffset)
            throw new InvalidDataException(
                $"Invalid PAP offsets: HavokOffset {FileHeader.HavokOffset} > FooterOffset {FileHeader.FooterOffset}");

        if (FileHeader.FooterOffset > dataLength)
            throw new InvalidDataException(
                $"Invalid PAP offsets: FooterOffset {FileHeader.FooterOffset} > data length {dataLength}");

        if (FileHeader.InfoOffset > dataLength)
            throw new InvalidDataException(
                $"Invalid PAP offsets: InfoOffset {FileHeader.InfoOffset} > data length {dataLength}");
    }

    [StructLayout(LayoutKind.Sequential, Size = 40)]
    public unsafe struct PapAnimation
    {
        public fixed byte Name[32];
        public ushort Type;
        public short HavokIndex;
        public bool IsFace;
        
        public string GetName => GetNameString();
        
        private string GetNameString()
        {
            fixed (byte* ptr = Name)
            {
                return Encoding.UTF8.GetString(ptr, 32).TrimEnd('\0');
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct PapFileHeader
    {
        public uint Magic;
        public uint Version;
        public ushort AnimationCount;
        public ushort ModelId;
        public SkeletonType ModelType;
        public byte Variant;
        public uint InfoOffset;
        public uint HavokOffset;
        public uint FooterOffset;
    }

    public enum SkeletonType : byte
    {
        Human = 0,
        Monster = 1,
        DemiHuman = 2,
        Weapon = 3,
    }
}
