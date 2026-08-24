using Meddle.Formats.Files;

namespace Meddle.Utils.Tests;

[TestClass]
public sealed class SklbFileTests
{
    private const uint Magic = 0x736B6C62; // "sklb"
    private const uint OldVersion = 0x31313030; // "1100"
    private const uint NewVersion = 0x31333030; // "1300"

    [TestMethod]
    public void KnownOldHeader_ValidOffset()
    {
        // magic(4) + version(4) + layer int16(2) + skeleton uint16(2) = 12
        var data = CreateSklb(OldVersion, skeletonOffset: 12, payloadLength: 4);

        var file = new SklbFile(data);

        Assert.AreEqual(Magic, file.Header.Magic);
        Assert.AreEqual(12, file.RawHeader.Length);
        Assert.AreEqual(4, file.Skeleton.Length);
    }

    [TestMethod]
    public void KnownNewHeader_ValidOffset()
    {
        // magic(4) + version(4) + layer uint32(4) + skeleton uint32(4) = 16
        var data = CreateSklb(NewVersion, skeletonOffset: 16, payloadLength: 8, oldHeader: false);

        var file = new SklbFile(data);

        Assert.AreEqual(Magic, file.Header.Magic);
        Assert.AreEqual(16, file.RawHeader.Length);
        Assert.AreEqual(8, file.Skeleton.Length);
    }

    [TestMethod]
    public void SkeletonOffsetBeforeHeaderEnd_Throws()
    {
        var data = CreateSklb(OldVersion, skeletonOffset: 8, payloadLength: 4);

        var ex = Assert.ThrowsExactly<InvalidDataException>(() => new SklbFile(data));

        StringAssert.Contains(ex.Message, "before header end");
    }

    [TestMethod]
    public void SkeletonOffsetPastEnd_Throws()
    {
        var data = CreateSklb(OldVersion, skeletonOffset: 100, payloadLength: 0);

        var ex = Assert.ThrowsExactly<InvalidDataException>(() => new SklbFile(data));

        StringAssert.Contains(ex.Message, "exceeds data length");
    }

    [TestMethod]
    public void UnknownVersion_Throws()
    {
        var data = CreateSklb(0x39393939, skeletonOffset: 12, payloadLength: 0);

        Assert.ThrowsExactly<InvalidDataException>(() => new SklbFile(data));
    }

    private static byte[] CreateSklb(uint version, uint skeletonOffset, int payloadLength, bool oldHeader = true)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(Magic);
        writer.Write(version);
        if (oldHeader)
        {
            writer.Write((short)0);
            writer.Write((ushort)skeletonOffset);
        }
        else
        {
            writer.Write(0u);
            writer.Write(skeletonOffset);
        }

        writer.Write(new byte[payloadLength]);
        writer.Flush();
        return stream.ToArray();
    }
}
