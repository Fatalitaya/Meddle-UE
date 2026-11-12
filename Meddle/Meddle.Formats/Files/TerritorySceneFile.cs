using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace Meddle.Formats.Files;

/// <summary>File-owned scene data shared by LVB/SGB (SCN1) and LGB (LGP1).</summary>
public sealed class TerritorySceneFile
{
    public record Filter(uint Key, ushort TerritoryId, ushort ContentId);
    public readonly record struct Festival(ushort Id, ushort Phase);
    public record Instance(uint Type, uint Id, string Name, Vector3 Translation, Vector3 Rotation,
        Vector3 Scale, string Path, uint EventObjectId)
    {
        public uint RuntimeType => Type == 45 ? 6u : Type;
        public Matrix4x4 LocalMatrix => Matrix4x4.CreateScale(Scale) * Matrix4x4.CreateRotationX(Rotation.X)
            * Matrix4x4.CreateRotationY(Rotation.Y) * Matrix4x4.CreateRotationZ(Rotation.Z)
            * Matrix4x4.CreateTranslation(Translation);
    }
    public record Layer(uint GroupId, uint Id, string Name, Festival Festival, uint FilterOperation,
        uint[] FilterKeys, Instance[] Instances)
    {
        // The runtime identifiers retain only the low 16 bits of the file's IDs.
        public ushort RuntimeKey => unchecked((ushort)Id);
        public ushort RuntimeGroupKey => unchecked((ushort)GroupId);
        public bool Matches(uint key, IReadOnlySet<Festival> festivals) =>
            (Festival.Id == 0 || festivals.Contains(Festival)) && FilterOperation switch
            {
                0 => true,
                1 => FilterKeys.Contains(key),
                2 => !FilterKeys.Contains(key),
                _ => false
            };
    }

    public List<Layer> Layers { get; } = [];
    public List<Filter> Filters { get; } = [];
    public List<string> LayerResources { get; } = [];
    public string TerrainPath { get; private set; } = "";
    private readonly byte[] data;

    public TerritorySceneFile(byte[] data)
    {
        this.data = data;
        var magic = U32(0);
        if (magic is not (0x3142474c or 0x3142564c or 0x31424753))
            throw new InvalidDataException("Expected LGB1, LVB1 or SGB1.");
        var offset = 12;
        var count = Count(8);
        for (var i = 0; i < count; i++)
        {
            var size = I32(offset + 4);
            if (size < 8 || offset > data.Length - size) throw new InvalidDataException("Invalid scene chunk.");
            switch (U32(offset))
            {
                case 0x3150474c: ReadGroup(offset + 8); break;
                case 0x314e4353: ReadScene(offset + 8); break;
            }
            offset = checked(offset + size);
        }
    }

    private void ReadScene(int start)
    {
        var groups = Relative(start, 0);
        for (var i = 0; i < Count(start + 4); i++) ReadGroup(groups + i * 16);
        if (I32(start + 8) != 0)
        {
            var general = Relative(start, 8);
            TerrainPath = StringAt(general, 4);
        }
        if (I32(start + 12) != 0)
        {
            var list = Relative(start, 12);
            var entries = Relative(list, 0);
            for (var i = 0; i < Count(list + 4); i++)
            {
                var item = entries + i * 28;
                Filters.Add(new Filter(U32(item + 4), U16(item + 16), U16(item + 18)));
            }
        }
        var paths = Relative(start, 20);
        for (var i = 0; i < Count(start + 24); i++) LayerResources.Add(StringAt(paths, i * 4));
    }

    private void ReadGroup(int start)
    {
        var groupId = U32(start);
        var offsets = Relative(start, 8);
        for (var i = 0; i < Count(start + 12); i++)
        {
            var layer = Relative(offsets, i * 4);
            uint operation = 0;
            uint[] keys = [];
            if (I32(layer + 20) != 0)
            {
                var filter = Relative(layer, 20);
                operation = U32(filter);
                var entries = Relative(filter, 4);
                keys = Enumerable.Range(0, Count(filter + 8)).Select(n => U32(entries + n * 4)).ToArray();
            }
            var instances = new Instance[Count(layer + 12)];
            var instanceOffsets = Relative(layer, 8);
            for (var j = 0; j < instances.Length; j++)
            {
                var item = Relative(instanceOffsets, j * 4);
                var type = U32(item);
                instances[j] = new Instance(type, U32(item + 4), StringAt(item, 8),
                    Vector(item + 12), Vector(item + 24), Vector(item + 36),
                    type is 1 or 6 ? StringAt(item, 48) : "", type == 45 ? U32(item + 48) : 0);
            }
            Layers.Add(new Layer(groupId, U32(layer), StringAt(layer, 4),
                new Festival(U16(layer + 24), U16(layer + 26)), operation, keys, instances));
        }
    }

    private int Relative(int start, int field) => checked(start + I32(start + field));
    private int Count(int offset)
    {
        var count = I32(offset);
        if (count < 0 || count > data.Length / 4) throw new InvalidDataException("Invalid scene count.");
        return count;
    }
    private ReadOnlySpan<byte> Slice(int offset, int size)
    {
        if (offset < 0 || offset > data.Length - size) throw new InvalidDataException("Scene offset outside file.");
        return data.AsSpan(offset, size);
    }
    private int I32(int offset) => BinaryPrimitives.ReadInt32LittleEndian(Slice(offset, 4));
    private uint U32(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(Slice(offset, 4));
    private ushort U16(int offset) => BinaryPrimitives.ReadUInt16LittleEndian(Slice(offset, 2));
    private float Float(int offset) => BitConverter.Int32BitsToSingle(I32(offset));
    private Vector3 Vector(int offset) => new(Float(offset), Float(offset + 4), Float(offset + 8));
    private string StringAt(int start, int field)
    {
        if (I32(start + field) == 0) return "";
        var offset = Relative(start, field);
        Slice(offset, 1);
        var end = Array.IndexOf(data, (byte)0, offset);
        if (end < 0) throw new InvalidDataException("Unterminated scene string.");
        return Encoding.UTF8.GetString(data, offset, end - offset);
    }
}
