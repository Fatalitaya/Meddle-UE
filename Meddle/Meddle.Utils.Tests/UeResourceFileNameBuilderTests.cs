using Meddle.Utils.Export.Ue;

namespace Meddle.Utils.Tests;

[TestClass]
public sealed class UeResourceFileNameBuilderTests
{
    [TestMethod]
    public void NormalPath_UsesSourceBasenameAndHash8()
    {
        var sha256 = UeResourceIdentity.GetSha256("material"u8);
        var builder = new UeResourceFileNameBuilder();

        var fileName = builder.Build(
            UeResourceType.Mtrl,
            "chara/equipment/e9156/material/v0001/mt_top.mtrl",
            sha256);

        Assert.AreEqual($"mt_top_{sha256[..8]}.mtrl", fileName);
    }

    [TestMethod]
    public void DecoratedRuntimePath_StripsHandlePrefix()
    {
        var sha256 = UeResourceIdentity.GetSha256("material"u8);
        var builder = new UeResourceFileNameBuilder();

        var fileName = builder.Build(
            UeResourceType.Mtrl,
            "|1_2_B6550A25_2B27|chara/equipment/e9156/material/v0001/mt_top.mtrl",
            sha256);

        Assert.AreEqual($"mt_top_{sha256[..8]}.mtrl", fileName);
    }

    [TestMethod]
    public void MalformedResolvedPath_UsesLogicalPathFallback()
    {
        var sha256 = UeResourceIdentity.GetSha256("material"u8);
        var builder = new UeResourceFileNameBuilder();

        var fileName = builder.Build(
            UeResourceType.Mtrl,
            "|broken",
            sha256,
            fallbackPath: "chara/equipment/e9156/material/v0001/mt_top.mtrl");

        Assert.AreEqual($"mt_top_{sha256[..8]}.mtrl", fileName);
    }

    [TestMethod]
    public void RuntimeGeneratedResource_UsesRuntimeKindPrefix()
    {
        var sha256 = UeResourceIdentity.GetSha256("png"u8);
        var builder = new UeResourceFileNameBuilder();

        var fileName = builder.Build(
            UeResourceType.Tex,
            "|broken",
            sha256,
            fallbackPath: "chara/decal.tex",
            runtimeKind: "decal");

        Assert.AreEqual($"runtime_decal_{sha256[..8]}.png", fileName);
    }

    [TestMethod]
    public void RuntimeGeneratedResource_WithoutKind_UsesRuntimeTexture()
    {
        var sha256 = UeResourceIdentity.GetSha256("png"u8);
        var builder = new UeResourceFileNameBuilder();

        var fileName = builder.Build(
            UeResourceType.Tex,
            "|broken",
            sha256,
            runtimeKind: "");

        Assert.AreEqual($"runtime_texture_{sha256[..8]}.png", fileName);
    }

    [TestMethod]
    public void RuntimeGeneratedResource_UnknownSource_UsesRuntimeResource()
    {
        var sha256 = UeResourceIdentity.GetSha256("png"u8);
        var builder = new UeResourceFileNameBuilder();

        var fileName = builder.Build(
            UeResourceType.Tex,
            "|broken",
            sha256,
            runtimeKind: "PenumbraFallback");

        Assert.AreEqual($"runtime_resource_{sha256[..8]}.png", fileName);
    }

    [TestMethod]
    public void RuntimeGeneratedResource_TextureLikeSource_UsesRuntimeTexture()
    {
        var sha256 = UeResourceIdentity.GetSha256("png"u8);
        var builder = new UeResourceFileNameBuilder();

        var fileName = builder.Build(
            UeResourceType.Tex,
            "|broken",
            sha256,
            runtimeKind: "FallbackFromDiskTexture");

        Assert.AreEqual($"runtime_texture_{sha256[..8]}.png", fileName);
    }

    [TestMethod]
    public void InvalidFileNameCharacters_AreReplaced()
    {
        var sha256 = UeResourceIdentity.GetSha256("material"u8);
        var builder = new UeResourceFileNameBuilder();

        var fileName = builder.Build(UeResourceType.Mtrl, "foo:bar?.mtrl", sha256);

        Assert.AreEqual($"foo_bar__{sha256[..8]}.mtrl", fileName);
    }

    [TestMethod]
    public void Hash8Collision_FallsBackToHash16()
    {
        var sha1 = new string('a', 8) + new string('1', 56);
        var sha2 = sha1[..8] + new string('b', 8) + new string('2', 48);
        var builder = new UeResourceFileNameBuilder();

        var first = builder.Build(UeResourceType.Mtrl, "mt_top.mtrl", sha1);
        var second = builder.Build(UeResourceType.Mtrl, "mt_top.mtrl", sha2);

        Assert.AreEqual($"mt_top_{sha1[..8]}.mtrl", first);
        Assert.AreEqual($"mt_top_{sha2[..16]}.mtrl", second);
    }

    [TestMethod]
    public void Hash16Collision_FallsBackToFullSha()
    {
        var sha1 = new string('a', 8) + new string('1', 56);
        var sha2 = sha1[..8] + new string('b', 8) + new string('2', 48);
        var sha3 = sha2[..16] + new string('c', 48);
        var builder = new UeResourceFileNameBuilder();

        var first = builder.Build(UeResourceType.Mtrl, "mt_top.mtrl", sha1);
        var second = builder.Build(UeResourceType.Mtrl, "mt_top.mtrl", sha2);
        var third = builder.Build(UeResourceType.Mtrl, "mt_top.mtrl", sha3);

        Assert.AreEqual($"mt_top_{sha1[..8]}.mtrl", first);
        Assert.AreEqual($"mt_top_{sha2[..16]}.mtrl", second);
        Assert.AreEqual($"mt_top_{sha3}.mtrl", third);
    }

    [TestMethod]
    public void SameSha_ReusesAllocatedFileName()
    {
        var sha256 = UeResourceIdentity.GetSha256("material"u8);
        var builder = new UeResourceFileNameBuilder();

        var first = builder.Build(UeResourceType.Mtrl, "mt_top.mtrl", sha256);
        var second = builder.Build(UeResourceType.Mtrl, "chara/other.mtrl", sha256);

        Assert.AreEqual(first, second);
    }

    [TestMethod]
    public void MissingPath_UsesResourceTypeFallback()
    {
        var sha256 = UeResourceIdentity.GetSha256("material"u8);
        var builder = new UeResourceFileNameBuilder();

        var fileName = builder.Build(UeResourceType.Mtrl, null, sha256);

        Assert.AreEqual($"mtrl_{sha256[..8]}.mtrl", fileName);
    }

    [TestMethod]
    public void ExcessivelyLongBaseName_IsTruncated()
    {
        var sha256 = UeResourceIdentity.GetSha256("material"u8);
        var builder = new UeResourceFileNameBuilder();

        var fileName = builder.Build(
            UeResourceType.Mtrl,
            $"{new string('a', 200)}.mtrl",
            sha256);

        Assert.AreEqual($"{new string('a', UeResourceFileNameBuilder.MaxBaseNameLength)}_{sha256[..8]}.mtrl", fileName);
    }
}
