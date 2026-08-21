using System.Text.Json;
using Meddle.Utils.Export.Ue;

namespace Meddle.Utils.Tests;

[TestClass]
public sealed class UeSnapshotSerializationTests
{
    [TestMethod]
    public void Serialize_UsesCamelCaseAndNoPointerFields()
    {
        var snapshot = CreateSnapshot("model-root", "model-child");
        snapshot.Root!.Models[0].Materials.Add(new UeMaterialSnapshot
        {
            ResourceId = "sha256:material",
            ShaderPackageResourceId = "sha256:shpk",
            ColorTable = new UeModernColorTableSetSnapshot
            {
                ColorTable = [new UeColorTableRowSnapshot()]
            }
        });

        var json = UeExportJson.Serialize(snapshot);

        StringAssert.Contains(json, "\"schemaVersion\"");
        StringAssert.Contains(json, "\"root\"");
        StringAssert.Contains(json, "\"attaches\"");
        Assert.IsFalse(json.Contains("captureStatus", StringComparison.Ordinal));
        Assert.IsFalse(json.Contains("ModelAddress", StringComparison.Ordinal));
        Assert.IsFalse(json.Contains("IntPtr", StringComparison.Ordinal));
        Assert.IsFalse(json.Contains("\"nint\"", StringComparison.Ordinal));
        Assert.IsFalse(json.Contains("\"header\"", StringComparison.Ordinal));
    }

    [TestMethod]
    public void CollectReferencedResourceIds_IncludesNestedAttachIds()
    {
        var snapshot = CreateSnapshot("model-root", "model-child");
        snapshot.Root!.Skeleton = new UeSkeletonSnapshot
        {
            PartialSkeletons = [new UePartialSkeletonSnapshot { ResourceId = "sha256:sklb" }]
        };
        snapshot.Root.Attach = new UeAttachSnapshot
        {
            OwnerSkeleton = new UeSkeletonSnapshot
            {
                PartialSkeletons = [new UePartialSkeletonSnapshot { ResourceId = "sha256:owner-sklb" }]
            },
            TargetSkeleton = new UeSkeletonSnapshot
            {
                PartialSkeletons = [new UePartialSkeletonSnapshot { ResourceId = "sha256:target-sklb" }]
            }
        };
        snapshot.Root.Models[0].Materials.Add(new UeMaterialSnapshot
        {
            ResourceId = "sha256:mtrl",
            ShaderPackageResourceId = "sha256:shpk",
            Textures =
            [
                new UeTextureSnapshot { ResourceId = "sha256:tex" }
            ],
            RenderOverrides = new UeRenderOverrideSnapshot
            {
                DecalTextureResourceId = "sha256:decal",
                SkinMaterialTextures =
                [
                    new UeSkinMaterialTextureSnapshot { ResourceId = "sha256:skin" }
                ]
            }
        });
        snapshot.Root.Models[0].Deformation = new UeDeformerSnapshot
        {
            PbdResource = new UePbdResourceSnapshot
            {
                ResourceId = "sha256:pbd",
                Availability = UeResourceAvailability.Available
            }
        };

        var ids = UeExportValidator.CollectReferencedResourceIds(snapshot);

        CollectionAssert.AreEquivalent(
            new[]
            {
                "model-root",
                "model-child",
                "sha256:sklb",
                "sha256:owner-sklb",
                "sha256:target-sklb",
                "sha256:mtrl",
                "sha256:shpk",
                "sha256:tex",
                "sha256:decal",
                "sha256:skin",
                "sha256:pbd"
            },
            ids.ToArray());
    }

    [TestMethod]
    public void CollectReferencedResourceIds_IgnoresEmptyPartialSkeletonSlot()
    {
        var snapshot = new UeCharacterSnapshot
        {
            Root = new UeCharacterNodeSnapshot
            {
                NodeId = "root",
                Skeleton = new UeSkeletonSnapshot
                {
                    PartialSkeletons =
                    [
                        new UePartialSkeletonSnapshot
                        {
                            ResourceId = null
                        }
                    ]
                }
            }
        };

        var ids = UeExportValidator.CollectReferencedResourceIds(snapshot);

        Assert.AreEqual(0, ids.Count);
    }

    [TestMethod]
    public void EmptyPartialSkeleton_PreservesSlotMetadataAndNullResourceId()
    {
        var snapshot = new UeCharacterSnapshot
        {
            Root = new UeCharacterNodeSnapshot
            {
                NodeId = "root",
                Skeleton = new UeSkeletonSnapshot
                {
                    PartialSkeletons =
                    [
                        new UePartialSkeletonSnapshot
                        {
                            Index = 3,
                            LogicalPath = null,
                            ResolvedPath = null,
                            ResourceId = null,
                            PartialSkeletonFlagsRaw = 0x12,
                            ConnectedBoneIndex = -1,
                            BoneCount = 0
                        }
                    ]
                }
            }
        };

        using var document = JsonDocument.Parse(UeExportJson.Serialize(snapshot));
        var partial = document.RootElement
                              .GetProperty("root")
                              .GetProperty("skeleton")
                              .GetProperty("partialSkeletons")[0];

        Assert.AreEqual(3, partial.GetProperty("index").GetInt32());
        Assert.AreEqual(0x12u, partial.GetProperty("partialSkeletonFlagsRaw").GetUInt32());
        Assert.AreEqual(-1, partial.GetProperty("connectedBoneIndex").GetInt32());
        Assert.AreEqual(0u, partial.GetProperty("boneCount").GetUInt32());
        Assert.AreEqual(JsonValueKind.Null, partial.GetProperty("logicalPath").ValueKind);
        Assert.AreEqual(JsonValueKind.Null, partial.GetProperty("resolvedPath").ValueKind);
        Assert.AreEqual(JsonValueKind.Null, partial.GetProperty("resourceId").ValueKind);
    }

    [TestMethod]
    public void Validator_RejectsMissingManifestReference()
    {
        var snapshot = CreateSnapshot("model-root", null);
        var manifest = new UeResourceManifest();

        var ex = Assert.ThrowsExactly<UeExportException>(() =>
            UeExportValidator.Validate(snapshot, manifest, Directory.GetCurrentDirectory()));

        StringAssert.Contains(ex.Message, "model-root");
    }

    [TestMethod]
    public void Validator_EmptySnapshotAndManifest_Pass()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"meddle-ue-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDirectory);
            UeExportValidator.Validate(
                new UeCharacterSnapshot
                {
                    Root = new UeCharacterNodeSnapshot { NodeId = "root" }
                },
                new UeResourceManifest(),
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
    public void Validator_RejectsSnapshotWithoutRoot()
    {
        Assert.ThrowsExactly<UeExportException>(() =>
            UeExportValidator.Validate(new UeCharacterSnapshot(), new UeResourceManifest(), Directory.GetCurrentDirectory()));
    }

    [TestMethod]
    public void Validator_ValidatesWrittenFlatResources()
    {
        var builder = new UeResourceManifestBuilder();
        var modelReference = builder.Add(new UeResourceCandidate
        {
            Type = UeResourceType.Mdl,
            LogicalPath = "chara/test.mdl",
            ResolvedPath = "chara/test.mdl",
            Data = "mdl"u8.ToArray(),
            Origin = UeResourceOrigin.Game
        });
        var skeletonReference = builder.Add(new UeResourceCandidate
        {
            Type = UeResourceType.Sklb,
            LogicalPath = "chara/test.sklb",
            ResolvedPath = "chara/test.sklb",
            Data = "sklb"u8.ToArray(),
            Origin = UeResourceOrigin.Game
        });
        var bundle = builder.BuildBundle();
        var snapshot = new UeCharacterSnapshot
        {
            Root = new UeCharacterNodeSnapshot
            {
                NodeId = "root",
                Models = [new UeModelSnapshot { Id = "root/model-0", ResourceId = modelReference.Id }],
                Skeleton = new UeSkeletonSnapshot
                {
                    PartialSkeletons = [new UePartialSkeletonSnapshot { ResourceId = skeletonReference.Id }]
                }
            }
        };

        var tempDirectory = Path.Combine(Path.GetTempPath(), $"meddle-ue-{Guid.NewGuid():N}");
        try
        {
            var resourcesDirectory = Path.Combine(tempDirectory, "resources");
            Directory.CreateDirectory(resourcesDirectory);
            foreach (var entry in bundle.Manifest.Resources)
            {
                File.WriteAllBytes(
                    Path.Combine(resourcesDirectory, Path.GetFileName(entry.File!)),
                    bundle.DataById[entry.Id]);
            }

            UeExportValidator.Validate(snapshot, bundle.Manifest, tempDirectory);
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
    public void Validator_RejectsDirectoryPreservingPath()
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
        bundle.Manifest.Resources[0].File = "resources/chara/test.mdl";

        var ex = Assert.ThrowsExactly<UeExportException>(() =>
            UeExportValidator.Validate(
                new UeCharacterSnapshot
                {
                    Root = new UeCharacterNodeSnapshot
                    {
                        NodeId = "root",
                        Models = [new UeModelSnapshot { Id = "root/model-0", ResourceId = reference.Id }]
                    }
                },
                bundle.Manifest,
                Directory.GetCurrentDirectory()));

        StringAssert.Contains(ex.Message, "flat resources directory");
    }

    [TestMethod]
    public void EquipmentModelId_RawValueSurvivesSerialization()
    {
        const ulong rawValue = 0x0102030405060708UL;
        var snapshot = new UeCharacterSnapshot
        {
            Root = new UeCharacterNodeSnapshot
            {
                NodeId = "root",
                Customize = new UeCustomizeSnapshot
                {
                    EquipmentModelIds =
                    [
                        new UeEquipmentModelIdSnapshot
                        {
                            Slot = 0,
                            Id = 0x0102,
                            Variant = 0x03,
                            Stain0 = 0x04,
                            Stain1 = 0x05,
                            Value = rawValue
                        }
                    ]
                }
            }
        };

        using var document = JsonDocument.Parse(UeExportJson.Serialize(snapshot));
        var value = document.RootElement
                            .GetProperty("root")
                            .GetProperty("customize")
                            .GetProperty("equipmentModelIds")[0]
                            .GetProperty("value")
                            .GetUInt64();

        Assert.AreEqual(rawValue, value);
    }

    [TestMethod]
    public void TextureRuntimeMetadata_SurvivesSerialization()
    {
        var snapshot = new UeCharacterSnapshot
        {
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
                                        RuntimeMetadata = new UeTextureRuntimeMetadata
                                        {
                                            Format = "R8G8B8A8UNorm",
                                            Width = 64,
                                            Height = 32,
                                            MipLevels = 6,
                                            ArraySize = 1,
                                            Dimension = "Tex2D",
                                            MiscFlags = "None"
                                        }
                                    }
                                ]
                            }
                        ]
                    }
                ]
            }
        };

        using var document = JsonDocument.Parse(UeExportJson.Serialize(snapshot));
        var metadata = document.RootElement
                               .GetProperty("root")
                               .GetProperty("models")[0]
                               .GetProperty("materials")[0]
                               .GetProperty("textures")[0]
                               .GetProperty("runtimeMetadata");

        Assert.AreEqual("R8G8B8A8UNorm", metadata.GetProperty("format").GetString());
        Assert.AreEqual(64u, metadata.GetProperty("width").GetUInt32());
        Assert.AreEqual(6u, metadata.GetProperty("mipLevels").GetUInt32());
        Assert.AreEqual("Tex2D", metadata.GetProperty("dimension").GetString());
    }

    [TestMethod]
    public void RenderOverrideFallback_PreservesAttemptedPathProvenance()
    {
        var snapshot = new UeCharacterSnapshot
        {
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
                                RenderOverrides = new UeRenderOverrideSnapshot
                                {
                                    DecalTextureLogicalPath = "chara/decal.tex",
                                    DecalTextureResolvedPath = "|broken",
                                    DecalTextureFallbackReason = "ResourceUnresolved",
                                    DecalTextureResourceId = "sha256:runtime"
                                }
                            }
                        ]
                    }
                ]
            }
        };

        using var document = JsonDocument.Parse(UeExportJson.Serialize(snapshot));
        var overrides = document.RootElement
                                .GetProperty("root")
                                .GetProperty("models")[0]
                                .GetProperty("materials")[0]
                                .GetProperty("renderOverrides");

        Assert.AreEqual("chara/decal.tex", overrides.GetProperty("decalTextureLogicalPath").GetString());
        Assert.AreEqual("|broken", overrides.GetProperty("decalTextureResolvedPath").GetString());
        Assert.AreEqual("ResourceUnresolved", overrides.GetProperty("decalTextureFallbackReason").GetString());
        Assert.AreEqual("sha256:runtime", overrides.GetProperty("decalTextureResourceId").GetString());
    }

    [TestMethod]
    public void Deformer_UsesRawObservedFields()
    {
        var snapshot = new UeCharacterSnapshot
        {
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
                            Status = UeDeformerStatus.Observed,
                            PbdResourcePath = "chara/xls/boneDeformer/human.pbd",
                            PbdResource = new UePbdResourceSnapshot
                            {
                                ResourceId = "sha256:pbd",
                                Availability = UeResourceAvailability.Available
                            },
                            RaceSexId = 801,
                            DeformerId = 701
                        }
                    }
                ]
            }
        };

        var json = UeExportJson.Serialize(snapshot);
        using var document = JsonDocument.Parse(json);
        var deformation = document.RootElement
                                  .GetProperty("root")
                                  .GetProperty("models")[0]
                                  .GetProperty("deformation");

        Assert.AreEqual(UeDeformerStatus.Observed, deformation.GetProperty("status").GetString());
        Assert.AreEqual("sha256:pbd", deformation.GetProperty("pbdResource").GetProperty("resourceId").GetString());
        Assert.AreEqual("Available", deformation.GetProperty("pbdResource").GetProperty("availability").GetString());
        Assert.AreEqual(801, deformation.GetProperty("raceSexId").GetInt32());
        Assert.AreEqual(701, deformation.GetProperty("deformerId").GetInt32());
        Assert.IsFalse(json.Contains("\"pbdResourceId\"", StringComparison.Ordinal));
        Assert.IsFalse(json.Contains("\"pbdResourceStatus\"", StringComparison.Ordinal));
        Assert.IsFalse(json.Contains("sourceRace", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(json.Contains("targetRace", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void Deformer_ObservedPbdResourceUnavailable_KeepsObservation()
    {
        var builder = new UeResourceManifestBuilder();
        var pbdObservation = builder.AddObservation(
            "chara/xls/boneDeformer/human.pbd",
            "chara/xls/boneDeformer/human.pbd",
            UeResourceType.Pbd,
            UeResourceOrigin.Game,
            UeResourceAvailability.Unavailable,
            "ResourceUnavailable");
        var manifest = builder.BuildBundle().Manifest;
        manifest.Observations.Add(new UeExportObservation
        {
            Code = "ResourceUnavailable",
            Message = "PBD bytes were unavailable.",
            NodeId = "root",
            ModelId = "root/model-0",
            ResourceId = pbdObservation.Id
        });

        var snapshot = new UeCharacterSnapshot
        {
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
                            Status = UeDeformerStatus.Observed,
                            PbdResourcePath = "chara/xls/boneDeformer/human.pbd",
                            PbdResource = new UePbdResourceSnapshot
                            {
                                ResourceId = pbdObservation.Id,
                                Availability = UeResourceAvailability.Unavailable
                            },
                            RaceSexId = 1401,
                            DeformerId = 13
                        }
                    }
                ]
            }
        };

        var tempDirectory = Path.Combine(Path.GetTempPath(), $"meddle-ue-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDirectory);
            UeExportValidator.Validate(snapshot, manifest, tempDirectory);

            var deformation = snapshot.Root!.Models[0].Deformation!;
            Assert.AreEqual(UeDeformerStatus.Observed, deformation.Status);
            Assert.AreEqual(UeResourceAvailability.Unavailable, deformation.PbdResource!.Availability);
            Assert.AreEqual(pbdObservation.Id, deformation.PbdResource.ResourceId);
            Assert.AreEqual(1401, deformation.RaceSexId);
            Assert.AreEqual(13, deformation.DeformerId);
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
    public void DocumentMetadata_ExistsOnlyOnDocumentRoot()
    {
        var root = CreateSnapshot("model-root", "model-child");
        root.Generator = new UeGeneratorSnapshot
        {
            Version = "1.2.3",
            Commit = null,
            Dirty = true,
            BuildId = "test-build-id"
        };

        var json = UeExportJson.Serialize(root);
        using var document = JsonDocument.Parse(json);

        Assert.AreEqual(4, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.AreEqual("1.2.3", document.RootElement.GetProperty("generator").GetProperty("version").GetString());
        Assert.AreEqual("root", document.RootElement.GetProperty("root").GetProperty("nodeId").GetString());
        var child = document.RootElement.GetProperty("root").GetProperty("attaches")[0];
        Assert.IsFalse(child.TryGetProperty("schemaVersion", out _));
        Assert.IsFalse(child.TryGetProperty("generator", out _));
        Assert.IsFalse(child.TryGetProperty("knownCaptureGaps", out _));
    }

    [TestMethod]
    public void AttachedCharacterNode_RetainsCustomizeAndModelMetadata()
    {
        var root = new UeCharacterSnapshot
        {
            Root = new UeCharacterNodeSnapshot
            {
                NodeId = "root",
                Attaches =
                [
                    new UeCharacterNodeSnapshot
                    {
                        NodeId = "root/attach-0",
                        Capture = new UeCaptureSnapshot
                        {
                            CapturedAtUtc = DateTime.UtcNow,
                            ModelType = "Weapon",
                            GenderRace = null
                        },
                        Customize = new UeCustomizeSnapshot
                        {
                            EquipmentModelIds =
                            [
                                new UeEquipmentModelIdSnapshot
                                {
                                    Slot = 0,
                                    Id = 1,
                                    Value = 123
                                }
                            ]
                        },
                        Models =
                        [
                            new UeModelSnapshot
                            {
                                Id = "root/attach-0/model-0",
                                SlotIndex = 0,
                                ModelType = "Weapon",
                                Enabled = true
                            }
                        ]
                    }
                ]
            }
        };

        using var document = JsonDocument.Parse(UeExportJson.Serialize(root));
        var child = document.RootElement.GetProperty("root").GetProperty("attaches")[0];

        Assert.AreEqual("Weapon", child.GetProperty("capture").GetProperty("modelType").GetString());
        Assert.AreEqual(JsonValueKind.Null, child.GetProperty("capture").GetProperty("genderRace").ValueKind);
        Assert.AreEqual(123ul, child.GetProperty("customize").GetProperty("equipmentModelIds")[0].GetProperty("value").GetUInt64());
        Assert.IsTrue(child.GetProperty("models")[0].GetProperty("enabled").GetBoolean());
    }

    [TestMethod]
    public void Generator_SerializesProvenanceFieldsOnly()
    {
        var snapshot = new UeCharacterSnapshot
        {
            Generator = new UeGeneratorSnapshot
            {
                Version = "1.2.3",
                Commit = null,
                Dirty = true,
                BuildId = "test-build-id"
            },
            Root = new UeCharacterNodeSnapshot { NodeId = "root" }
        };

        var json = UeExportJson.Serialize(snapshot);
        using var document = JsonDocument.Parse(json);
        var generator = document.RootElement.GetProperty("generator");

        Assert.AreEqual("Meddle-UE", generator.GetProperty("name").GetString());
        Assert.AreEqual("1.2.3", generator.GetProperty("version").GetString());
        Assert.AreEqual(JsonValueKind.Null, generator.GetProperty("commit").ValueKind);
        Assert.IsTrue(generator.GetProperty("dirty").GetBoolean());
        Assert.AreEqual("test-build-id", generator.GetProperty("buildId").GetString());
        Assert.IsFalse(json.Contains("compatibility", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void Schema_UsesCentralCurrentVersion()
    {
        Assert.AreEqual(UeExportSchema.CurrentVersion, new UeCharacterSnapshot().SchemaVersion);
        Assert.AreEqual(UeExportSchema.CurrentVersion, new UeResourceManifest().SchemaVersion);
        var currentVersion = UeExportSchema.CurrentVersion;
        Assert.AreEqual(4, currentVersion);
    }

    [TestMethod]
    public void Schema_ManifestBuilderEmitsCurrentVersion()
    {
        var bundle = new UeResourceManifestBuilder().BuildBundle();

        Assert.AreEqual(UeExportSchema.CurrentVersion, bundle.Manifest.SchemaVersion);
    }

    [TestMethod]
    public void Validator_RejectsDuplicateNodeId()
    {
        var snapshot = new UeCharacterSnapshot
        {
            Root = new UeCharacterNodeSnapshot
            {
                NodeId = "root",
                Attaches =
                [
                    new UeCharacterNodeSnapshot { NodeId = "root/attach-0" },
                    new UeCharacterNodeSnapshot { NodeId = "root/attach-0" }
                ]
            }
        };

        var ex = Assert.ThrowsExactly<UeExportException>(() =>
            UeExportValidator.Validate(snapshot, new UeResourceManifest(), Directory.GetCurrentDirectory()));

        StringAssert.Contains(ex.Message, "duplicate nodeId");
    }

    [TestMethod]
    public void ExportPaths_RejectsExistingDestination()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"meddle-ue-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDirectory);
            Assert.ThrowsExactly<UeExportException>(() =>
                UeExportPaths.EnsureDestinationDoesNotExist(tempDirectory));

            var filePath = Path.Combine(tempDirectory, "bundle.complete");
            File.WriteAllText(filePath, "complete");
            Assert.ThrowsExactly<UeExportException>(() =>
                UeExportPaths.EnsureDestinationDoesNotExist(filePath));
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, true);
            }
        }
    }

    private static UeCharacterSnapshot CreateSnapshot(string? rootModelId, string? childModelId)
    {
        var root = new UeCharacterNodeSnapshot
        {
            NodeId = "root",
            Models = [new UeModelSnapshot { Id = "root/model-0", ResourceId = rootModelId ?? "model-root" }]
        };

        if (childModelId != null)
        {
            root.Attaches.Add(new UeCharacterNodeSnapshot
            {
                NodeId = "root/attach-0",
                Models = [new UeModelSnapshot { Id = "root/attach-0/model-0", ResourceId = childModelId }]
            });
        }

        return new UeCharacterSnapshot
        {
            SchemaVersion = UeExportSchema.CurrentVersion,
            Root = root
        };
    }
}
