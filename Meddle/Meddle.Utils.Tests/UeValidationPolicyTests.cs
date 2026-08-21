using System.Text.Json;
using Meddle.Utils.Export.Ue;

namespace Meddle.Utils.Tests;

[TestClass]
public sealed class UeValidationPolicyTests
{
    [TestMethod]
    public void ReadPolicy_MapsStatusToAvailabilityAndObservationCode()
    {
        Assert.AreEqual(UeResourceAvailability.Available, UeResourceReadPolicy.GetAvailability(UeResourceReadStatus.Success));
        Assert.AreEqual(UeResourceAvailability.Unavailable, UeResourceReadPolicy.GetAvailability(UeResourceReadStatus.NotFound));
        Assert.AreEqual(UeResourceAvailability.Unavailable, UeResourceReadPolicy.GetAvailability(UeResourceReadStatus.ReadFailed));
        Assert.AreEqual(UeResourceAvailability.Unresolved, UeResourceReadPolicy.GetAvailability(UeResourceReadStatus.Unresolved));
        Assert.AreEqual("ResourceUnavailable", UeResourceReadPolicy.GetObservationCode(UeResourceReadStatus.NotFound));
        Assert.AreEqual("ResourceReadFailed", UeResourceReadPolicy.GetObservationCode(UeResourceReadStatus.ReadFailed));
        Assert.AreEqual("ResourceUnresolved", UeResourceReadPolicy.GetObservationCode(UeResourceReadStatus.Unresolved));
    }

    [TestMethod]
    public void UnavailableObservation_PassesStructuralValidatorWithoutDiagnostics()
    {
        var builder = new UeResourceManifestBuilder();
        var observation = builder.AddObservation(
            "chara/missing.mdl",
            "chara/missing.mdl",
            UeResourceType.Mdl,
            UeResourceOrigin.Game,
            UeResourceAvailability.Unavailable,
            "ResourceUnavailable");
        var manifest = builder.BuildBundle().Manifest;
        manifest.Observations.Add(new UeExportObservation
        {
            Code = "ResourceUnavailable",
            Message = "Model bytes were unavailable.",
            Reason = "ResourceUnavailable",
            NodeId = "root",
            ModelId = "root/model-0",
            ResourceId = observation.Id
        });
        var snapshot = CreateRootSnapshot(observation.Id);

        var tempDirectory = Path.Combine(Path.GetTempPath(), $"meddle-ue-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDirectory);
            UeExportValidator.Validate(snapshot, manifest, tempDirectory);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, true);
            }
        }
    }

    [TestMethod]
    public void StructuralValidator_RejectsUnavailableEntryWithPhysicalMetadata()
    {
        var manifest = new UeResourceManifest
        {
            Resources =
            [
                new UeResourceEntry
                {
                    Id = "observation:abc",
                    Type = "mdl",
                    Availability = UeResourceAvailability.Unavailable,
                    LogicalPath = "chara/missing.mdl",
                    ResolvedPath = "chara/missing.mdl",
                    File = "resources/foo.mdl",
                    Sha256 = "abc",
                    Size = 1,
                    Origin = "game"
                }
            ]
        };

        var ex = Assert.ThrowsExactly<UeExportException>(() =>
            UeExportValidator.Validate(
                CreateRootSnapshot("observation:abc"),
                manifest,
                Directory.GetCurrentDirectory()));

        StringAssert.Contains(ex.Message, "declares file, sha256, or size");
    }

    [TestMethod]
    public void StructuralValidator_RejectsDanglingSnapshotReference()
    {
        var ex = Assert.ThrowsExactly<UeExportException>(() =>
            UeExportValidator.Validate(
                CreateRootSnapshot("observation:abc"),
                new UeResourceManifest(),
                Directory.GetCurrentDirectory()));

        StringAssert.Contains(ex.Message, "missing from the manifest");
    }

    [TestMethod]
    public void StructuralValidator_RejectsObservationEventWithMissingResourceId()
    {
        var manifest = new UeResourceManifest
        {
            Observations =
            [
                new UeExportObservation
                {
                    Code = "ResourceUnavailable",
                    Reason = "ResolverUnavailable",
                    NodeId = "root",
                    ResourceId = "observation:missing"
                }
            ]
        };

        var ex = Assert.ThrowsExactly<UeExportException>(() =>
            UeExportValidator.Validate(
                CreateRootSnapshot(),
                manifest,
                Directory.GetCurrentDirectory()));

        StringAssert.Contains(ex.Message, "missing from the manifest");
    }

    [TestMethod]
    public void StructuralValidator_RejectsObservationEventWithoutNodeId()
    {
        var manifest = new UeResourceManifest
        {
            Observations =
            [
                new UeExportObservation
                {
                    Code = "DeformerNotObserved",
                    Message = "No observation."
                }
            ]
        };

        var ex = Assert.ThrowsExactly<UeExportException>(() =>
            UeExportValidator.Validate(
                CreateRootSnapshot(),
                manifest,
                Directory.GetCurrentDirectory()));

        StringAssert.Contains(ex.Message, "known nodeId");
    }

    [TestMethod]
    public void Completer_WritesCompleteForUnavailableObservation()
    {
        var builder = new UeResourceManifestBuilder();
        var observation = builder.AddObservation(
            "chara/missing.mdl",
            "chara/missing.mdl",
            UeResourceType.Mdl,
            UeResourceOrigin.Game,
            UeResourceAvailability.Unavailable,
            "ResourceUnavailable");
        var bundle = builder.BuildBundle();
        bundle.Manifest.Observations.Add(new UeExportObservation
        {
            Code = "ResourceUnavailable",
            Message = "Model bytes were unavailable.",
            Reason = "ResolverUnavailable",
            NodeId = "root",
            ModelId = "root/model-0",
            ResourceId = observation.Id
        });
        var snapshot = CreateRootSnapshot(observation.Id);

        var parent = Path.Combine(Path.GetTempPath(), $"meddle-ue-{Guid.NewGuid():N}");
        Directory.CreateDirectory(parent);
        var destination = Path.Combine(parent, "bundle");
        try
        {
            var output = UeExportCompleter.Complete(snapshot, bundle, destination);

            Assert.AreEqual(destination, output);
            Assert.IsTrue(File.Exists(Path.Combine(destination, "bundle.complete")));
            var manifestJson = File.ReadAllText(Path.Combine(destination, "manifest.json"));
            StringAssert.Contains(manifestJson, "\"availability\": \"Unavailable\"");
            Assert.IsFalse(manifestJson.Contains("severity", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(destination))
            {
                Directory.Delete(destination, true);
            }

            if (Directory.Exists(parent))
            {
                Directory.Delete(parent, true);
            }
        }
    }

    [TestMethod]
    public void Completer_StructuralContradictionDoesNotComplete()
    {
        var manifest = new UeResourceManifest
        {
            Resources =
            [
                new UeResourceEntry
                {
                    Id = "observation:bad",
                    Type = "mdl",
                    Availability = UeResourceAvailability.Unavailable,
                    LogicalPath = "chara/missing.mdl",
                    ResolvedPath = "chara/missing.mdl",
                    File = "resources/foo.mdl",
                    Sha256 = "abc",
                    Size = 1,
                    Origin = "game"
                }
            ]
        };
        var bundle = new UeResourceBundle
        {
            Manifest = manifest,
            DataById = new Dictionary<string, byte[]>()
        };
        var snapshot = CreateRootSnapshot("observation:bad");

        var parent = Path.Combine(Path.GetTempPath(), $"meddle-ue-{Guid.NewGuid():N}");
        Directory.CreateDirectory(parent);
        var destination = Path.Combine(parent, "bundle");
        try
        {
            Assert.ThrowsExactly<UeExportException>(() =>
                UeExportCompleter.Complete(snapshot, bundle, destination));

            Assert.IsFalse(Directory.Exists(destination));
        }
        finally
        {
            if (Directory.Exists(parent))
            {
                Directory.Delete(parent, true);
            }
        }
    }

    [TestMethod]
    public void Completer_RejectsPhysicalFileCollisionWithDifferentSha()
    {
        var data1 = "first"u8.ToArray();
        var data2 = "second"u8.ToArray();
        var sha1 = UeResourceIdentity.GetSha256(data1);
        var sha2 = UeResourceIdentity.GetSha256(data2);
        var manifest = new UeResourceManifest
        {
            Resources =
            [
                new UeResourceEntry
                {
                    Id = UeResourceIdentity.CreateResourceId(sha1, UeResourceType.Mdl),
                    Type = "mdl",
                    Availability = UeResourceAvailability.Available,
                    LogicalPath = "chara/a.mdl",
                    ResolvedPath = "chara/a.mdl",
                    File = "resources/duplicate.mdl",
                    Sha256 = sha1,
                    Size = data1.LongLength,
                    Origin = "game"
                },
                new UeResourceEntry
                {
                    Id = UeResourceIdentity.CreateResourceId(sha2, UeResourceType.Mdl),
                    Type = "mdl",
                    Availability = UeResourceAvailability.Available,
                    LogicalPath = "chara/b.mdl",
                    ResolvedPath = "chara/b.mdl",
                    File = "resources/duplicate.mdl",
                    Sha256 = sha2,
                    Size = data2.LongLength,
                    Origin = "game"
                }
            ]
        };
        var bundle = new UeResourceBundle
        {
            Manifest = manifest,
            DataById = new Dictionary<string, byte[]>
            {
                [manifest.Resources[0].Id] = data1,
                [manifest.Resources[1].Id] = data2
            }
        };

        var parent = Path.Combine(Path.GetTempPath(), $"meddle-ue-{Guid.NewGuid():N}");
        Directory.CreateDirectory(parent);
        var destination = Path.Combine(parent, "bundle");
        try
        {
            var ex = Assert.ThrowsExactly<UeExportException>(() =>
                UeExportCompleter.Complete(CreateRootSnapshot(), bundle, destination));

            StringAssert.Contains(ex.Message, "multiple SHA-256 values");
            Assert.IsFalse(Directory.Exists(destination));
        }
        finally
        {
            if (Directory.Exists(parent))
            {
                Directory.Delete(parent, true);
            }
        }
    }

    [TestMethod]
    public void ValidationPolicy_StructuralOnlyIgnoresUnavailable()
    {
        var (snapshot, manifest) = CreateUnavailableMdlBundle();

        var result = UeValidationPolicy.StructuralOnly.Evaluate(snapshot, manifest);

        Assert.IsTrue(result.IsSatisfied);
        Assert.AreEqual(0, result.Issues.Count);
    }

    [TestMethod]
    public void ValidationPolicy_ForResourceIds_FailsOnlyWhenConsumerRequires()
    {
        var (snapshot, manifest) = CreateUnavailableMdlBundle();
        var resourceId = manifest.Resources.Single().Id;

        var unrelated = UeValidationPolicy.ForResourceTypes(UeResourceType.Tex).Evaluate(snapshot, manifest);
        var requiring = UeValidationPolicy.ForResourceIds(resourceId).Evaluate(snapshot, manifest);

        Assert.IsTrue(unrelated.IsSatisfied);
        Assert.IsFalse(requiring.IsSatisfied);
        Assert.AreEqual(1, requiring.Issues.Count);
        Assert.AreEqual(resourceId, requiring.Issues[0].ResourceId);
    }

    [TestMethod]
    public void ValidationPolicy_ForResourceTypes_FailsOnlyWhenConsumerRequires()
    {
        var (snapshot, manifest) = CreateUnavailableMdlBundle();

        var unrelated = UeValidationPolicy.ForResourceTypes(UeResourceType.Tex).Evaluate(snapshot, manifest);
        var requiring = UeValidationPolicy.ForResourceTypes(UeResourceType.Mdl).Evaluate(snapshot, manifest);

        Assert.IsTrue(unrelated.IsSatisfied);
        Assert.IsFalse(requiring.IsSatisfied);
        Assert.AreEqual(1, requiring.Issues.Count);
    }

    [TestMethod]
    public void ValidationPolicy_ForResourceIds_ReportsMissingId()
    {
        var (snapshot, manifest) = CreateUnavailableMdlBundle();

        var result = UeValidationPolicy.ForResourceIds("sha256:not-in-manifest:mdl").Evaluate(snapshot, manifest);

        Assert.IsFalse(result.IsSatisfied);
        Assert.AreEqual("sha256:not-in-manifest:mdl", result.Issues[0].ResourceId);
    }

    [TestMethod]
    public void ValidationPolicy_ComposedEvaluation_RunsStructuralValidationFirst()
    {
        var manifest = new UeResourceManifest
        {
            Resources =
            [
                new UeResourceEntry
                {
                    Id = "observation:abc",
                    Type = "mdl",
                    Availability = UeResourceAvailability.Unavailable,
                    LogicalPath = "chara/missing.mdl",
                    ResolvedPath = "chara/missing.mdl",
                    File = "resources/foo.mdl",
                    Sha256 = "abc",
                    Size = 1,
                    Origin = "game"
                }
            ]
        };

        Assert.ThrowsExactly<UeExportException>(() =>
            UeValidationPolicy.StructuralOnly.Evaluate(
                CreateRootSnapshot("observation:abc"),
                manifest,
                Directory.GetCurrentDirectory()));
    }

    [TestMethod]
    public void Snapshot_NoLongerSerializesCaptureStatus()
    {
        var json = UeExportJson.Serialize(CreateRootSnapshot());

        using var document = JsonDocument.Parse(json);
        Assert.IsFalse(document.RootElement.TryGetProperty("captureStatus", out _));
    }

    [TestMethod]
    public void AvailableEntry_WrongIdFailsValidator()
    {
        var builder = new UeResourceManifestBuilder();
        var reference = builder.Add(new UeResourceCandidate
        {
            Type = UeResourceType.Mdl,
            LogicalPath = "chara/test.mdl",
            ResolvedPath = "chara/test.mdl",
            Data = "mdl"u8.ToArray(),
            Origin = UeResourceOrigin.Game
        });
        var bundle = builder.BuildBundle();
        bundle.Manifest.Resources[0].Id = $"sha256:{new string('0', 64)}:mdl";

        var ex = Assert.ThrowsExactly<UeExportException>(() =>
            UeExportValidator.Validate(
                CreateRootSnapshot(bundle.Manifest.Resources[0].Id),
                bundle.Manifest,
                Directory.GetCurrentDirectory()));

        StringAssert.Contains(ex.Message, "content id does not match expected");
    }

    [TestMethod]
    public void AvailableEntry_HumanReadableFileNameAndCorrectSha_PassesValidator()
    {
        var data = "mdl"u8.ToArray();
        var sha256 = UeResourceIdentity.GetSha256(data);
        var fileName = new UeResourceFileNameBuilder().Build(
            UeResourceType.Mdl,
            "chara/equipment/e9156/model/v0001/c0101e0001_top.mdl",
            sha256);
        var relativeFile = $"resources/{fileName}";
        var manifest = new UeResourceManifest
        {
            Resources =
            [
                new UeResourceEntry
                {
                    Id = UeResourceIdentity.CreateResourceId(sha256, UeResourceType.Mdl),
                    Type = "mdl",
                    Availability = UeResourceAvailability.Available,
                    LogicalPath = "chara/equipment/e9156/model/v0001/c0101e0001_top.mdl",
                    ResolvedPath = "chara/equipment/e9156/model/v0001/c0101e0001_top.mdl",
                    File = relativeFile,
                    Sha256 = sha256,
                    Size = data.LongLength,
                    Origin = "game",
                    Aliases = []
                }
            ]
        };

        var tempDirectory = Path.Combine(Path.GetTempPath(), $"meddle-ue-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(tempDirectory, "resources"));
            File.WriteAllBytes(Path.Combine(tempDirectory, relativeFile), data);

            UeExportValidator.Validate(
                CreateRootSnapshot(manifest.Resources[0].Id),
                manifest,
                tempDirectory);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, true);
            }
        }
    }

    [TestMethod]
    public void AvailableEntry_IncorrectShaFailsValidator()
    {
        var data = "mdl"u8.ToArray();
        var wrongSha = new string('a', 63) + "b";
        var manifest = new UeResourceManifest
        {
            Resources =
            [
                new UeResourceEntry
                {
                    Id = UeResourceIdentity.CreateResourceId(wrongSha, UeResourceType.Mdl),
                    Type = "mdl",
                    Availability = UeResourceAvailability.Available,
                    LogicalPath = "chara/test.mdl",
                    ResolvedPath = "chara/test.mdl",
                    File = "resources/human_readable.mdl",
                    Sha256 = wrongSha,
                    Size = data.LongLength,
                    Origin = "game",
                    Aliases = []
                }
            ]
        };

        var tempDirectory = Path.Combine(Path.GetTempPath(), $"meddle-ue-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(tempDirectory, "resources"));
            File.WriteAllBytes(Path.Combine(tempDirectory, "resources/human_readable.mdl"), data);

            var ex = Assert.ThrowsExactly<UeExportException>(() =>
                UeExportValidator.Validate(
                    CreateRootSnapshot(manifest.Resources[0].Id),
                    manifest,
                    tempDirectory));

            StringAssert.Contains(ex.Message, "SHA-256 mismatch");
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, true);
            }
        }
    }

    [TestMethod]
    public void StructuralValidator_RejectsMismatchedManifestSchemaVersion()
    {
        var manifest = new UeResourceManifest
        {
            SchemaVersion = UeExportSchema.CurrentVersion + 1
        };

        var ex = Assert.ThrowsExactly<UeExportException>(() =>
            UeExportValidator.Validate(
                CreateRootSnapshot(),
                manifest,
                Directory.GetCurrentDirectory()));

        StringAssert.Contains(ex.Message, "does not match snapshot schema version");
    }

    [TestMethod]
    public void StructuralValidator_RejectsObsoleteDeformerStatus()
    {
        var snapshot = new UeCharacterSnapshot
        {
            SchemaVersion = UeExportSchema.CurrentVersion,
            Root = new UeCharacterNodeSnapshot
            {
                NodeId = "root",
                Models =
                [
                    new UeModelSnapshot
                    {
                        Id = "root/model-0",
                        Deformation = new UeDeformerSnapshot
                        {
                            Status = "StaticResolved"
                        }
                    }
                ]
            }
        };

        var ex = Assert.ThrowsExactly<UeExportException>(() =>
            UeExportValidator.Validate(
                snapshot,
                new UeResourceManifest(),
                Directory.GetCurrentDirectory()));

        StringAssert.Contains(ex.Message, "obsolete or unsupported deformer status");
    }

    [TestMethod]
    public void StructuralValidator_RejectsRuntimeTextureWithoutRuntimeGeneratedOrigin()
    {
        var builder = new UeResourceManifestBuilder();
        var reference = builder.Add(new UeResourceCandidate
        {
            Type = UeResourceType.Tex,
            LogicalPath = "chara/runtime.tex",
            ResolvedPath = "chara/runtime.tex",
            Data = [1, 2, 3],
            Origin = UeResourceOrigin.Game,
            RuntimeTexture = new UeRuntimeTextureMetadata
            {
                Encoding = "png",
                Width = 1,
                Height = 1
            }
        });
        var bundle = builder.BuildBundle();
        var snapshot = new UeCharacterSnapshot
        {
            SchemaVersion = UeExportSchema.CurrentVersion,
            Root = new UeCharacterNodeSnapshot
            {
                NodeId = "root",
                Models =
                [
                    new UeModelSnapshot
                    {
                        Id = "root/model-0",
                        Materials =
                        [
                            new UeMaterialSnapshot
                            {
                                Textures =
                                [
                                    new UeTextureSnapshot
                                    {
                                        ResourceId = reference.Id
                                    }
                                ]
                            }
                        ]
                    }
                ]
            }
        };

        var ex = Assert.ThrowsExactly<UeExportException>(() =>
            UeExportValidator.Validate(
                snapshot,
                bundle.Manifest,
                Directory.GetCurrentDirectory()));

        StringAssert.Contains(ex.Message, "expected runtimegenerated");
    }

    private static (UeCharacterSnapshot Snapshot, UeResourceManifest Manifest) CreateUnavailableMdlBundle()
    {
        var builder = new UeResourceManifestBuilder();
        var observation = builder.AddObservation(
            "chara/missing.mdl",
            "chara/missing.mdl",
            UeResourceType.Mdl,
            UeResourceOrigin.Game,
            UeResourceAvailability.Unavailable,
            "ResourceUnavailable");
        var manifest = builder.BuildBundle().Manifest;
        manifest.Observations.Add(new UeExportObservation
        {
            Code = "ResourceUnavailable",
            Reason = "ResolverUnavailable",
            NodeId = "root",
            ModelId = "root/model-0",
            ResourceId = observation.Id
        });
        return (CreateRootSnapshot(observation.Id), manifest);
    }

    private static UeCharacterSnapshot CreateRootSnapshot(string? resourceId = null)
    {
        return new UeCharacterSnapshot
        {
            Root = new UeCharacterNodeSnapshot
            {
                NodeId = "root",
                Models =
                [
                    new UeModelSnapshot
                    {
                        Id = "root/model-0",
                        ResourceId = resourceId
                    }
                ]
            }
        };
    }
}
