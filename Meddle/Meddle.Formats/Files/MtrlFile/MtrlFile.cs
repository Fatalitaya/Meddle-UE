using Meddle.Formats;

namespace Meddle.Formats.Files.MtrlFile;

public class MtrlFile
{
    public const uint MtrlMagic = 0x1030000;

    private readonly byte[] data;

    public MaterialFileHeader FileHeader;
    public TextureOffset[] TextureOffsets;
    public UvColorSet[] UvColorSets;
    public ColorSet[] ColorSets;
    public byte[] Strings;
    public byte[] AdditionalData;
    public byte[] DataSet;
    
    //public ColorTable ColorTable;
    //public ColorDyeTable ColorDyeTable;
    
    public MaterialShaderHeader ShaderHeader;
    public ShaderKey[] ShaderKeys;
    public Constant[] Constants;
    public Sampler[] Samplers;
    public uint[] ShaderValues;

    public MtrlFile(byte[] data) : this((ReadOnlySpan<byte>)data) { }

    public MtrlFile(ReadOnlySpan<byte> data)
    {
        this.data = data.ToArray();
        var reader = new SpanBinaryReader(data);
        FileHeader = reader.Read<MaterialFileHeader>();
        if (FileHeader.Version != MtrlMagic)
            throw new InvalidDataException($"Invalid MTRL magic: {FileHeader.Version:X}");
        TextureOffsets = new TextureOffset[FileHeader.TextureCount];

        var offsets = reader.Read<uint>(FileHeader.TextureCount);
        for (var i = 0; i < offsets.Length; i++)
        {
            TextureOffsets[i].Offset = (ushort)offsets[i];
            TextureOffsets[i].Flags = (ushort)(offsets[i] >> 16);
        }

        UvColorSets = reader.Read<UvColorSet>(FileHeader.UvSetCount).ToArray();
        ColorSets = reader.Read<ColorSet>(FileHeader.ColorSetCount).ToArray();

        Strings = reader.Read<byte>(FileHeader.StringTableSize).ToArray();
        AdditionalData = reader.Read<byte>(FileHeader.AdditionalDataSize).ToArray();

        if (FileHeader.DataSetSize > 0)
        {
            DataSet = reader.Read<byte>(FileHeader.DataSetSize).ToArray();
            // var dataSetReader = new SpanBinaryReader(DataSet);
            // if (LargeColorTable)
            // {
            //     ColorTable = HasTable ? ColorTable.Load(ref dataSetReader) : ColorTable.Default();
            //     if (HasDyeTable)
            //         ColorDyeTable = dataSetReader.Read<ColorDyeTable>();
            // }
            // else
            // {
            //     ColorTable = HasTable ? ColorTable.LoadLegacy(ref dataSetReader) : ColorTable.DefaultLegacy();
            //     if (HasDyeTable)
            //         ColorDyeTable = new ColorDyeTable(dataSetReader.Read<LegacyColorDyeTable>());
            // }
        }
        else
        {
            DataSet = Array.Empty<byte>();
            //ColorTable = ColorTable.Default();
        }

        ShaderHeader = reader.Read<MaterialShaderHeader>();

        ShaderKeys = reader.Read<ShaderKey>(ShaderHeader.ShaderKeyCount).ToArray();
        Constants = reader.Read<Constant>(ShaderHeader.ConstantCount).ToArray();
        Samplers = reader.Read<Sampler>(ShaderHeader.SamplerCount).ToArray();

        ShaderValues = reader.Read<uint>(ShaderHeader.ShaderValueListSize / 4).ToArray();
    }

    // Width/height are log2 counts of RGBA half texels, packed into bits 4..11.
    public int TableDimensionLogs =>
        ((AdditionalData.Length > 0 ? AdditionalData[0] >> 4 : 0)
         | (AdditionalData.Length > 1 ? AdditionalData[1] << 4 : 0)) & 0xFF;

    public bool LargeColorTable => TableDimensionLogs == 0x53
        || (TableDimensionLogs == 0 && DataSet.Length >= ColorTable.Size);

    public bool HasTable => AdditionalData.Length > 0 && (AdditionalData[0] & 0x4) != 0;
    public bool HasDyeTable => AdditionalData.Length > 0 && (AdditionalData[0] & 0x8) != 0;
    public ReadOnlySpan<byte> RawData => data;
}

public struct Constant
{
    public uint ConstantId;
    public ushort ValueOffset;
    public ushort ValueSize;
}

public unsafe struct Sampler
{
    public uint SamplerId;
    public uint Flags;
    public byte TextureIndex;
    private fixed byte padding[3];

    public readonly TextureAddressMode AddressModeU => (TextureAddressMode)(Flags & 3);
    public readonly TextureAddressMode AddressModeV => (TextureAddressMode)((Flags >> 2) & 3);
    public readonly float LodBias
    {
        get
        {
            var raw = (int)((Flags >> 10) & 0x3FF);
            return (raw >= 512 ? raw - 1024 : raw) / 64f;
        }
    }
    public readonly uint MinLod => (Flags >> 20) & 0xF;
    public readonly uint UnknownFlags => Flags & ~0x00FFFC0Fu;
}

public enum TextureAddressMode
{
    Wrap = 0,
    Mirror = 1,
    Clamp = 2,
    Border = 3
}

public struct ShaderKey
{
    public uint Category;
    public uint Value;
}

public struct ColorSet
{
    public ushort NameOffset;
    public byte Index;
    public byte Unknown1;
}

public struct UvColorSet
{
    public ushort NameOffset;
    public byte Index;
    public byte Unknown1;
}

public struct TextureOffset
{
    public ushort Offset;
    public ushort Flags;
}

public struct MaterialFileHeader
{
    public uint Version;
    public ushort FileSize;
    public ushort DataSetSize;
    public ushort StringTableSize;
    public ushort ShaderPackageNameOffset;
    public byte TextureCount;
    public byte UvSetCount;
    public byte ColorSetCount;
    public byte AdditionalDataSize;
}

public struct MaterialShaderHeader
{
    public ushort ShaderValueListSize;
    public ushort ShaderKeyCount;
    public ushort ConstantCount;
    public ushort SamplerCount;
    public uint Flags;
}

[Flags]
public enum ShaderFlags : uint
{
    HideBackfaces = 0x01,
    EnableTranslucency = 0x10
}
