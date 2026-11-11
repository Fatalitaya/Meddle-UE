using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Meddle.Formats.Files;
using Meddle.Formats.Files.MtrlFile;
using Meddle.Formats.Helpers;

namespace Meddle.Utils.Tests;

[TestClass]
public sealed class AssetParsingTests
{
    [TestMethod]
    [DataRow(0, 0f)]
    [DataRow(1, 0.015625f)]
    [DataRow(511, 7.984375f)]
    [DataRow(512, -8f)]
    [DataRow(1023, -0.015625f)]
    public void Sampler_DecodesSignedTenBitBiasWithoutUnknownFlags(int raw, float expected)
    {
        var sampler = new Sampler { Flags = 0xA50003F0u | ((uint)raw << 10) | (15u << 20) | 2u | (3u << 2) };
        Assert.AreEqual(expected, sampler.LodBias);
        Assert.AreEqual(TextureAddressMode.Clamp, sampler.AddressModeU);
        Assert.AreEqual(TextureAddressMode.Border, sampler.AddressModeV);
        Assert.AreEqual(15u, sampler.MinLod);
        Assert.AreEqual(0xA50003F0u, sampler.UnknownFlags);
        Assert.AreEqual(12, Marshal.SizeOf<Sampler>());
    }

    [TestMethod]
    [DataRow(0x0C01)]
    [DataRow(0x0D01)]
    [DataRow(0x0E01)]
    public void Shpk_StagesPreserveFollowingParametersAndBytecode(int version)
    {
        var bytes = MakeShpk(version);
        var file = new ShpkFile(bytes);
        Assert.AreEqual(0x12345678u, file.MaterialParams.Single().Id);
        Assert.AreEqual(2.5f, file.MaterialParamDefaults.Single());
        Assert.AreEqual(91u, file.SubViewKeys[0].DefaultValue);
        CollectionAssert.AreEqual(new byte[] { 10, 20, 30, 40 }, file.VertexShaders[0].Blob);
        Assert.HasCount(8, file.VertexShaders[0].AdditionalHeader);
        Assert.AreEqual(version >= 0x0D01 ? 1 : 0, file.HullShaders.Length);
        foreach (var shader in file.HullShaders.Concat(file.DomainShaders).Concat(file.GeometryShaders))
        {
            CollectionAssert.AreEqual(new byte[] { 10, 20, 30, 40 }, shader.Blob);
            Assert.IsEmpty(shader.AdditionalHeader);
        }
        Assert.AreEqual(version >= 0x0E01 ? 1 : 0, file.NodeAliasClusters.Length);
        if (version >= 0x0E01)
        {
            var cluster = file.NodeAliasClusters.Single();
            Assert.AreEqual(92u, cluster.SubView2);
            Assert.AreEqual(91u, cluster.SubView1);
            var sub = cluster.SubClusters.Single();
            Assert.AreEqual((short)-3, sub.OwnIndex);
            Assert.AreEqual(123u, sub.Aliases.Single().Selector);
            Assert.AreEqual(456u, sub.Aliases.Single().Alias);
            Assert.HasCount(95, sub.ExtraData);
            Assert.AreEqual(999u, sub.ExtraData[^1]);
        }
        Assert.AreEqual((int)file.FileHeader.BlobsOffset, bytes.Length - file.RemainingData.Length);
    }

    [TestMethod]
    [DataRow(0x0534, 2048, true)]
    [DataRow(0xA534, 2048, true)]
    [DataRow(0x0004, 2048, true)]
    [DataRow(0x0004, 512, false)]
    [DataRow(0x0424, 512, false)]
    public void Mtrl_DimensionsUseOnlyTheirEightBits(int flags, int size, bool modern)
    {
        var file = MakeMtrl(flags, size);
        Assert.AreEqual(modern, file.LargeColorTable);
        var table = file.GetColorTable();
        if (modern) Assert.IsInstanceOfType<ColorTableSet>(table);
        else Assert.IsInstanceOfType<LegacyColorTableSet>(table);
    }

    [TestMethod]
    [DataRow(0x0544)]
    [DataRow(0x042C)]
    public void Mtrl_UnrecognizedLayoutRetainsRawData(int flags)
    {
        var file = MakeMtrl(flags, 2048);
        Assert.IsNull(file.GetColorTable());
        Assert.HasCount(2048, file.DataSet);
        Assert.AreEqual(0xAB, file.DataSet[^1]);
    }

    private static MtrlFile MakeMtrl(int flags, int size)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        Write(writer, new MaterialFileHeader { Version = MtrlFile.MtrlMagic,
            DataSetSize = (ushort)size, AdditionalDataSize = 4 });
        writer.Write(flags);
        var data = new byte[size];
        data[^1] = 0xAB;
        writer.Write(data);
        Write(writer, new MaterialShaderHeader());
        return new MtrlFile(stream.ToArray());
    }

    private static byte[] MakeShpk(int version)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(ShpkFile.ShPkMagic);
        Write(writer, new ShpkFile.ShpkHeader { Version = (uint)version,
            DxVersion = ShpkFile.DxVersion.Dx11, VertexShaderCount = 1, PixelShaderCount = 1,
            MaterialParamsSize = 4, MaterialParamCount = 1, HasMatParamDefaults = 1 });
        if (version >= 0x0D01) { writer.Write(1u); writer.Write(1u); writer.Write(1u); }
        if (version >= 0x0E01) writer.Write(1u);
        var count = version >= 0x0D01 ? 5 : 2;
        for (var i = 0; i < count; i++)
        {
            Write(writer, new ShpkFile.Shader.ShaderDefinition {
                BlobOffset = i == 0 ? 0u : 8u, BlobSize = i == 0 ? 12u : 4u });
            if (version >= 0x0D01) writer.Write(0u);
        }
        Write(writer, new ShpkFile.MaterialParam { Id = 0x12345678, ByteSize = 4 });
        writer.Write(2.5f);
        writer.Write(91u); writer.Write(92u);
        if (version >= 0x0E01)
        {
            writer.Write(92u); writer.Write(91u); writer.Write(1u); writer.Write(0u);
            writer.Write((short)-3); writer.Write((ushort)1);
            writer.Write(123u); writer.Write(456u);
            for (var i = 0; i < 95; i++) writer.Write(i == 94 ? 999u : 0u);
        }
        var blobsOffset = (uint)stream.Position;
        writer.Write(new byte[8]);
        writer.Write(new byte[] { 10, 20, 30, 40 });
        var bytes = stream.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), (uint)bytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), blobsOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(20), (uint)bytes.Length);
        return bytes;
    }

    private static void Write<T>(BinaryWriter writer, T value) where T : unmanaged =>
        writer.Write(MemoryMarshal.AsBytes(new[] { value }.AsSpan()));
}
