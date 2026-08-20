using Meddle.Utils.Export.Ue;

namespace Meddle.Utils.Tests;

[TestClass]
public sealed class UeResourceManifestBuilderTests
{
    [TestMethod]
    public void SameBytesAndType_DeduplicatesAndPreservesAlias()
    {
        var builder = new UeResourceManifestBuilder();
        var data = "same-content"u8.ToArray();

        var first = builder.Add(new UeResourceCandidate
        {
            Type = UeResourceType.Mdl,
            LogicalPath = "chara/a.mdl",
            ResolvedPath = "chara/a.mdl",
            Data = data,
            Origin = UeResourceOrigin.Game
        });
        var second = builder.Add(new UeResourceCandidate
        {
            Type = UeResourceType.Mdl,
            LogicalPath = "chara/b.mdl",
            ResolvedPath = "chara/b.mdl",
            Data = data,
            Origin = UeResourceOrigin.Game
        });

        var bundle = builder.BuildBundle();
        var entry = bundle.Manifest.Resources.Single();
        Assert.AreEqual(first.Id, second.Id);
        Assert.AreEqual(first.File, entry.File);
        Assert.AreEqual(1, entry.Aliases.Count);
        Assert.AreEqual("chara/b.mdl", entry.Aliases[0].LogicalPath);
        StringAssert.Contains(entry.File!, "a_");
        StringAssert.Contains(entry.File!, ".mdl");
    }

    [TestMethod]
    public void AvailableResource_UsesHumanReadableFileName()
    {
        var builder = new UeResourceManifestBuilder();
        var data = "material"u8.ToArray();

        builder.Add(new UeResourceCandidate
        {
            Type = UeResourceType.Mtrl,
            LogicalPath = "chara/equipment/e9156/material/v0001/mt_top.mtrl",
            ResolvedPath = "chara/equipment/e9156/material/v0001/mt_top.mtrl",
            Data = data,
            Origin = UeResourceOrigin.Game
        });

        var bundle = builder.BuildBundle();
        var sha256 = UeResourceIdentity.GetSha256(data);
        var entry = bundle.Manifest.Resources.Single();

        Assert.AreEqual($"resources/mt_top_{sha256[..8]}.mtrl", entry.File);
    }

    [TestMethod]
    public void DecoratedResolvedPath_DoesNotLeakHandlePrefixIntoFileName()
    {
        var builder = new UeResourceManifestBuilder();
        var data = "material"u8.ToArray();

        builder.Add(new UeResourceCandidate
        {
            Type = UeResourceType.Mtrl,
            LogicalPath = "chara/equipment/e9156/material/v0001/mt_top.mtrl",
            ResolvedPath = "|1_2_B6550A25_2B27|chara/equipment/e9156/material/v0001/mt_top.mtrl",
            Data = data,
            Origin = UeResourceOrigin.Game
        });

        var bundle = builder.BuildBundle();
        var sha256 = UeResourceIdentity.GetSha256(data);

        Assert.AreEqual($"resources/mt_top_{sha256[..8]}.mtrl", bundle.Manifest.Resources.Single().File);
    }

    [TestMethod]
    public void EndToEndManifest_UsesHumanReadableFileNameFromDecoratedResolvedPath()
    {
        var builder = new UeResourceManifestBuilder();
        var data = "abc"u8.ToArray();
        var reference = builder.Add(new UeResourceCandidate
        {
            Type = UeResourceType.Mtrl,
            LogicalPath = "chara/equipment/e9156/material/v0001/mt_top.mtrl",
            ResolvedPath = "|1_2_xxx|chara/equipment/e9156/material/v0001/mt_top.mtrl",
            Data = data,
            Origin = UeResourceOrigin.Game
        });

        var bundle = builder.BuildBundle();
        var sha256 = UeResourceIdentity.GetSha256(data);
        var entry = bundle.Manifest.Resources.Single();
        var expectedFile = $"resources/mt_top_{sha256[..8]}.mtrl";

        Assert.AreEqual(expectedFile, entry.File);
        Assert.AreEqual(expectedFile, reference.File);
        Assert.AreNotEqual($"resources/{sha256}.mtrl", entry.File);
        Assert.IsFalse(entry.File!.Contains('|'));
    }

    [TestMethod]
    public void DifferentBytes_CreatesSeparatePhysicalResources()
    {
        var builder = new UeResourceManifestBuilder();
        builder.Add(new UeResourceCandidate
        {
            Type = UeResourceType.Tex,
            LogicalPath = "chara/a.tex",
            ResolvedPath = "chara/a.tex",
            Data = "a"u8.ToArray(),
            Origin = UeResourceOrigin.Game
        });
        builder.Add(new UeResourceCandidate
        {
            Type = UeResourceType.Tex,
            LogicalPath = "chara/b.tex",
            ResolvedPath = "chara/b.tex",
            Data = "b"u8.ToArray(),
            Origin = UeResourceOrigin.Game
        });

        var bundle = builder.BuildBundle();

        Assert.AreEqual(2, bundle.Manifest.Resources.Count);
        Assert.AreEqual(2, bundle.DataById.Count);
        Assert.AreNotEqual(bundle.Manifest.Resources[0].File, bundle.Manifest.Resources[1].File);
    }

    [TestMethod]
    public void BuildBundle_SortsDeterministically()
    {
        var builder = new UeResourceManifestBuilder();
        builder.Add(Create("z/a.mdl", "b.mdl", UeResourceType.Mdl, "1"));
        builder.Add(Create("z/a.tex", "a.tex", UeResourceType.Tex, "2"));
        builder.Add(Create("z/a.mdl", "a.mdl", UeResourceType.Mdl, "3"));
        builder.Add(Create("z/a.shpk", "a.shpk", UeResourceType.Shpk, "4"));

        var bundle = builder.BuildBundle();
        var ordered = bundle.Manifest.Resources
                            .Select(x => $"{x.Type}|{x.LogicalPath}")
                            .ToArray();

        CollectionAssert.AreEqual(
            new[] { "mdl|z/a.mdl", "mdl|z/a.mdl", "shpk|z/a.shpk", "tex|z/a.tex" },
            ordered);
    }

    [TestMethod]
    public void Aliases_SortedDeterministically()
    {
        var builder = new UeResourceManifestBuilder();
        var data = "same"u8.ToArray();
        builder.Add(CreateWithData("chara/z.mdl", "chara/z.mdl", UeResourceType.Mdl, data));
        builder.Add(CreateWithData("chara/a.mdl", "chara/a.mdl", UeResourceType.Mdl, data));
        builder.Add(CreateWithData("chara/b.mdl", "chara/b.mdl", UeResourceType.Mdl, data));

        var bundle = builder.BuildBundle();
        var aliases = bundle.Manifest.Resources.Single().Aliases
                            .Select(x => x.LogicalPath)
                            .ToArray();

        CollectionAssert.AreEqual(
            new[] { "chara/a.mdl", "chara/b.mdl" },
            aliases);
    }

    [TestMethod]
    public void RuntimeGeneratedTexture_PreservesFallbackProvenance()
    {
        var builder = new UeResourceManifestBuilder();
        var data = "png"u8.ToArray();
        builder.Add(new UeResourceCandidate
        {
            Type = UeResourceType.Tex,
            LogicalPath = "chara/decal.tex",
            ResolvedPath = "|broken",
            Data = data,
            Origin = UeResourceOrigin.RuntimeGenerated,
            RuntimeTexture = new UeRuntimeTextureMetadata
            {
                Encoding = "png",
                Width = 64,
                Height = 64,
                Source = "decal",
                LogicalPath = "chara/decal.tex",
                ResolvedPath = "|broken",
                FallbackReason = "ResourceUnresolved"
            }
        });

        var bundle = builder.BuildBundle();
        var metadata = bundle.Manifest.Resources.Single().RuntimeTexture!;
        var sha256 = UeResourceIdentity.GetSha256(data);

        Assert.AreEqual("chara/decal.tex", metadata.LogicalPath);
        Assert.AreEqual("|broken", metadata.ResolvedPath);
        Assert.AreEqual("ResourceUnresolved", metadata.FallbackReason);
        Assert.AreEqual($"resources/runtime_decal_{sha256[..8]}.png", bundle.Manifest.Resources.Single().File);
    }

    [TestMethod]
    public void ObservationEntry_PreservesObservationMetadata()
    {
        var builder = new UeResourceManifestBuilder();
        var reference = builder.AddObservation(
            "chara/missing.mdl",
            "|mod|chara/missing.mdl",
            UeResourceType.Mdl,
            UeResourceOrigin.Modded,
            UeResourceAvailability.Unavailable,
            "ResourceUnavailable");

        var bundle = builder.BuildBundle();
        var entry = bundle.Manifest.Resources.Single();

        Assert.IsTrue(entry.Id.StartsWith("observation:", StringComparison.Ordinal));
        Assert.AreEqual(UeResourceAvailability.Unavailable, entry.Availability);
        Assert.AreEqual("modded", entry.Origin);
        Assert.AreEqual("ResourceUnavailable", entry.Reason);
        Assert.IsNull(entry.File);
        Assert.IsNull(entry.Sha256);
        Assert.IsNull(entry.Size);
        Assert.IsNull(reference.File);
    }

    [TestMethod]
    public void AddObservation_SameIdentityWithDifferentOrigin_Fails()
    {
        var builder = new UeResourceManifestBuilder();
        builder.AddObservation(
            "chara/missing.mdl",
            "chara/missing.mdl",
            UeResourceType.Mdl,
            UeResourceOrigin.Game,
            UeResourceAvailability.Unavailable,
            "ResourceUnavailable");

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            builder.AddObservation(
                "chara/missing.mdl",
                "chara/missing.mdl",
                UeResourceType.Mdl,
                UeResourceOrigin.Modded,
                UeResourceAvailability.Unavailable,
                "ResourceUnavailable"));
    }

    [TestMethod]
    public void AddObservation_SameIdentityWithDifferentAvailability_Fails()
    {
        var builder = new UeResourceManifestBuilder();
        builder.AddObservation(
            "chara/missing.mdl",
            "chara/missing.mdl",
            UeResourceType.Mdl,
            UeResourceOrigin.Game,
            UeResourceAvailability.Unavailable,
            "ResourceUnavailable");

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            builder.AddObservation(
                "chara/missing.mdl",
                "chara/missing.mdl",
                UeResourceType.Mdl,
                UeResourceOrigin.Game,
                UeResourceAvailability.Unresolved,
                "ResourceUnresolved"));
    }

    [TestMethod]
    public void AddObservation_WithoutSourcePath_Fails()
    {
        var builder = new UeResourceManifestBuilder();

        Assert.ThrowsExactly<ArgumentException>(() =>
            builder.AddObservation(
                null,
                null,
                UeResourceType.Mdl,
                UeResourceOrigin.Unknown,
                UeResourceAvailability.Unavailable));
    }

    private static UeResourceCandidate Create(string logicalPath, string resolvedPath, UeResourceType type, string content)
    {
        return CreateWithData(
            logicalPath,
            resolvedPath,
            type,
            System.Text.Encoding.UTF8.GetBytes(content));
    }

    private static UeResourceCandidate CreateWithData(string logicalPath, string resolvedPath, UeResourceType type, byte[] data)
    {
        return new UeResourceCandidate
        {
            Type = type,
            LogicalPath = logicalPath,
            ResolvedPath = resolvedPath,
            Data = data,
            Origin = UeResourceOrigin.Game
        };
    }
}
