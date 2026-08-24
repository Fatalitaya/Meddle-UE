using System.Runtime.InteropServices;
using Meddle.Formats.Files;

namespace Meddle.Utils.Tests;

[TestClass]
public sealed class PapFileTests
{
    [TestMethod]
    public void Constructor_AcceptsValidPapMagic()
    {
        var file = new PapFile(CreateHeader(PapFile.PapMagic, animationCount: 0));

        Assert.AreEqual(PapFile.PapMagic, file.FileHeader.Magic);
        Assert.AreEqual(0, file.Animations.Length);
    }

    [TestMethod]
    public void Constructor_RejectsInvalidPapMagic()
    {
        var ex = Assert.ThrowsExactly<InvalidDataException>(() =>
            new PapFile(CreateHeader(magic: 0, animationCount: 0)));

        StringAssert.Contains(ex.Message, "Invalid PAP magic");
        StringAssert.Contains(ex.Message, "actual 0x00000000");
    }

    [TestMethod]
    public void PapAnimation_HasFortyByteLayout()
    {
        Assert.AreEqual(40, Marshal.SizeOf<PapFile.PapAnimation>());
    }

    [TestMethod]
    public void HavokOffsetAfterFooter_Throws()
    {
        var ex = Assert.ThrowsExactly<InvalidDataException>(() =>
            new PapFile(CreateHeader(PapFile.PapMagic, animationCount: 0, havokOffset: 10, footerOffset: 5, dataLength: 20)));

        StringAssert.Contains(ex.Message, "HavokOffset");
    }

    [TestMethod]
    public void FooterOffsetPastEnd_Throws()
    {
        var ex = Assert.ThrowsExactly<InvalidDataException>(() =>
            new PapFile(CreateHeader(PapFile.PapMagic, animationCount: 0, footerOffset: 100, dataLength: 20)));

        StringAssert.Contains(ex.Message, "FooterOffset");
    }

    [TestMethod]
    public void InfoOffsetPastEnd_Throws()
    {
        var ex = Assert.ThrowsExactly<InvalidDataException>(() =>
            new PapFile(CreateHeader(PapFile.PapMagic, animationCount: 0, infoOffset: 100, dataLength: 20)));

        StringAssert.Contains(ex.Message, "InfoOffset");
    }

    [TestMethod]
    public void ValidOffsets_ExposeExpectedHavokSpan()
    {
        var file = new PapFile(CreateHeader(
            PapFile.PapMagic,
            animationCount: 0,
            infoOffset: 0,
            havokOffset: 20,
            footerOffset: 30,
            dataLength: 40));

        Assert.AreEqual(10, file.HavokData.Length);
        // total data = 26-byte header + 40 payload bytes
        Assert.AreEqual(36, file.FooterData.Length);
    }

    private static byte[] CreateHeader(
        uint magic,
        ushort animationCount,
        uint infoOffset = 0,
        uint havokOffset = 0,
        uint footerOffset = 0,
        int dataLength = 0)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write(magic);
        writer.Write(0u); // version
        writer.Write(animationCount);
        writer.Write((ushort)0); // model id
        writer.Write((byte)0); // model type
        writer.Write((byte)0); // variant
        writer.Write(infoOffset);
        writer.Write(havokOffset);
        writer.Write(footerOffset);
        writer.Write(new byte[dataLength]);
        writer.Flush();

        return stream.ToArray();
    }
}
