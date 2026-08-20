using Meddle.Utils.Export.Ue;

namespace Meddle.Utils.Tests;

[TestClass]
public sealed class UeResourceSourceTests
{
    [TestMethod]
    public void ParsedPath_KeepsHandleDecorationSeparateFromSourceKind()
    {
        var parsed = UeResolvedPath.TryParse("|1_2_B6550A25_2B27|chara/equipment/e9156/material/v0001/mt_c0201e9156_top_a.mtrl");

        Assert.IsNotNull(parsed);
        Assert.AreEqual("|1_2_B6550A25_2B27|chara/equipment/e9156/material/v0001/mt_c0201e9156_top_a.mtrl", parsed!.Original);
        Assert.AreEqual("chara/equipment/e9156/material/v0001/mt_c0201e9156_top_a.mtrl", parsed.Trimmed);
        Assert.IsTrue(parsed.HadHandleDecoration);
        Assert.IsFalse(parsed.IsRooted);
        Assert.AreEqual(UeResourceSourceKind.Game, parsed.SourceKind);
    }

    [TestMethod]
    public void VanillaPath_UsesSqPackAndIsGameOrigin()
    {
        var sqPackCalls = 0;
        var diskCalls = 0;
        var source = new UeResourceSource(
            path =>
            {
                sqPackCalls++;
                return path == "chara/test.mdl" ? "vanilla"u8.ToArray() : null;
            },
            path =>
            {
                diskCalls++;
                return null;
            });

        var result = source.Read(new UeResourceReadRequest(
            "chara/test.mdl",
            "chara/test.mdl",
            UeResourceType.Mdl));

        Assert.AreEqual(UeResourceReadStatus.Success, result.Status);
        Assert.AreEqual(UeResourceOrigin.Game, result.Origin);
        Assert.AreEqual("vanilla", System.Text.Encoding.UTF8.GetString(result.Data!));
        Assert.AreEqual(1, sqPackCalls);
        Assert.AreEqual(0, diskCalls);
    }

    [TestMethod]
    public void RootedDiskPath_ReadsActualDiskBytesAsModded()
    {
        var diskPath = Path.Combine(Path.GetTempPath(), "meddle-ue", "chara", "test.mdl");
        var sqPackCalls = 0;
        var diskCalls = 0;
        var source = new UeResourceSource(
            path =>
            {
                sqPackCalls++;
                return null;
            },
            path =>
            {
                diskCalls++;
                Assert.AreEqual(diskPath, path);
                return "modded"u8.ToArray();
            });

        var result = source.Read(new UeResourceReadRequest(
            "chara/test.mdl",
            diskPath,
            UeResourceType.Mdl));

        Assert.AreEqual(UeResourceReadStatus.Success, result.Status);
        Assert.AreEqual(UeResourceOrigin.Modded, result.Origin);
        Assert.AreEqual("modded", System.Text.Encoding.UTF8.GetString(result.Data!));
        Assert.AreEqual(0, sqPackCalls);
        Assert.AreEqual(1, diskCalls);
    }

    [TestMethod]
    public void PlainRootedDiskPath_ReadsActualDiskBytesAsModded()
    {
        var diskPath = Path.Combine(Path.GetTempPath(), "meddle-ue", "chara", "test.mdl");
        var sqPackCalls = 0;
        var diskCalls = 0;
        var source = new UeResourceSource(
            path =>
            {
                sqPackCalls++;
                return null;
            },
            path =>
            {
                diskCalls++;
                Assert.AreEqual(diskPath, path);
                return "modded"u8.ToArray();
            });

        var result = source.Read(new UeResourceReadRequest(
            "chara/test.mdl",
            diskPath,
            UeResourceType.Mdl));

        Assert.AreEqual(UeResourceReadStatus.Success, result.Status);
        Assert.AreEqual(UeResourceOrigin.Modded, result.Origin);
        Assert.AreEqual("modded", System.Text.Encoding.UTF8.GetString(result.Data!));
        Assert.AreEqual(0, sqPackCalls);
        Assert.AreEqual(1, diskCalls);
    }

    [TestMethod]
    public void DecoratedRelativePath_UsesSqPackAndIsGameOrigin()
    {
        var sqPackCalls = 0;
        var diskCalls = 0;
        var source = new UeResourceSource(
            path =>
            {
                sqPackCalls++;
                Assert.AreEqual("chara/test.mdl", path);
                return "runtime-game"u8.ToArray();
            },
            path =>
            {
                diskCalls++;
                return null;
            });

        var result = source.Read(new UeResourceReadRequest(
            "chara/test.mdl",
            "|mod|chara/test.mdl",
            UeResourceType.Mdl));

        Assert.AreEqual(UeResourceReadStatus.Success, result.Status);
        Assert.AreEqual(UeResourceOrigin.Game, result.Origin);
        Assert.AreEqual("runtime-game", System.Text.Encoding.UTF8.GetString(result.Data!));
        Assert.AreEqual(1, sqPackCalls);
        Assert.AreEqual(0, diskCalls);
    }

    [TestMethod]
    public void DecoratedRootedPath_ReadsActualDiskBytes()
    {
        var diskPath = Path.Combine(Path.GetTempPath(), "meddle-ue", "chara", "test.mdl");
        var source = new UeResourceSource(
            _ => "vanilla"u8.ToArray(),
            path =>
            {
                Assert.AreEqual(diskPath, path);
                return "modded"u8.ToArray();
            });

        var result = source.Read(new UeResourceReadRequest(
            "chara/test.mdl",
            $"|mod|{diskPath}",
            UeResourceType.Mdl));

        Assert.AreEqual(UeResourceReadStatus.Success, result.Status);
        Assert.AreEqual(UeResourceOrigin.Modded, result.Origin);
        Assert.AreEqual("modded", System.Text.Encoding.UTF8.GetString(result.Data!));
    }

    [TestMethod]
    public void DecoratedWindowsRootedPath_ReadsDiskWithoutSqPackFallback()
    {
        var sqPackCalls = 0;
        var source = new UeResourceSource(
            path =>
            {
                sqPackCalls++;
                return null;
            },
            path =>
            {
                Assert.AreEqual(@"C:\mods\file.mtrl", path);
                return "modded"u8.ToArray();
            });

        var result = source.Read(new UeResourceReadRequest(
            "chara/file.mtrl",
            "|abc|C:\\mods\\file.mtrl",
            UeResourceType.Mtrl));

        Assert.AreEqual(UeResourceReadStatus.Success, result.Status);
        Assert.AreEqual(UeResourceOrigin.Modded, result.Origin);
        Assert.AreEqual(0, sqPackCalls);
    }

    [TestMethod]
    [DataRow("|broken")]
    [DataRow("||")]
    [DataRow("|metadata|")]
    public void MalformedDecoratedPath_ReturnsUnresolvedWithoutReading(string path)
    {
        var source = new UeResourceSource(
            _ => throw new InvalidOperationException("SqPack should not be called."),
            _ => throw new InvalidOperationException("Disk should not be called."));

        var result = source.Read(new UeResourceReadRequest(path, path, UeResourceType.Mdl));

        Assert.AreEqual(UeResourceReadStatus.Unresolved, result.Status);
        Assert.AreEqual(UeResourceOrigin.Unknown, result.Origin);
        Assert.IsNull(result.Data);
    }

    [TestMethod]
    [DataRow(
        "|1_2_B6550A25_2B27|chara/equipment/e9156/material/v0001/mt_c0201e9156_top_a.mtrl",
        "chara/equipment/e9156/material/v0001/mt_c0201e9156_top_a.mtrl")]
    [DataRow(
        "|1_2_D381A62F_2B27|chara/human/c0201/obj/body/b0001/material/v0001/mt_c0201b0001_a.mtrl",
        "chara/human/c0201/obj/body/b0001/material/v0001/mt_c0201b0001_a.mtrl")]
    public void RealDecoratedMaterialPath_IsNotUnresolvedAndUsesSqPack(string resolvedPath, string lookupPath)
    {
        var sqPackCalls = 0;
        var source = new UeResourceSource(
            path =>
            {
                sqPackCalls++;
                Assert.AreEqual(lookupPath, path);
                return "material"u8.ToArray();
            },
            _ => null);

        var result = source.Read(new UeResourceReadRequest(
            Path.GetFileName(lookupPath),
            resolvedPath,
            UeResourceType.Mtrl));

        Assert.AreEqual(UeResourceReadStatus.Success, result.Status);
        Assert.AreEqual(UeResourceOrigin.Game, result.Origin);
        Assert.AreEqual(1, sqPackCalls);
    }

    [TestMethod]
    public void MissingResource_ReturnsNotFound()
    {
        var source = new UeResourceSource(_ => null, _ => null);

        var result = source.Read(new UeResourceReadRequest(
            "chara/test.mdl",
            "chara/test.mdl",
            UeResourceType.Mdl));

        Assert.AreEqual(UeResourceReadStatus.NotFound, result.Status);
        Assert.IsNull(result.Data);
    }

    [TestMethod]
    public void MissingPath_ReturnsMissingPath()
    {
        var source = new UeResourceSource(_ => null, _ => null);

        var result = source.Read(new UeResourceReadRequest(null, null, UeResourceType.Mdl));

        Assert.AreEqual(UeResourceReadStatus.MissingPath, result.Status);
        Assert.IsNull(result.Data);
    }

    [TestMethod]
    public void UnexpectedReaderException_IsNotSwallowedAsWarning()
    {
        var source = new UeResourceSource(
            _ => throw new InvalidOperationException("internal reader bug"),
            _ => null);

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            source.Read(new UeResourceReadRequest("chara/test.mdl", "chara/test.mdl", UeResourceType.Mdl)));
    }

    [TestMethod]
    public void ControlledIoFailure_RemainsReadFailed()
    {
        var source = new UeResourceSource(
            _ => throw new IOException("disk read failed"),
            _ => null);

        var result = source.Read(new UeResourceReadRequest(
            "chara/test.mdl",
            "chara/test.mdl",
            UeResourceType.Mdl));

        Assert.AreEqual(UeResourceReadStatus.ReadFailed, result.Status);
    }
}
