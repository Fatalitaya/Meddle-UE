using Meddle.Utils.Export.Ue;

namespace Meddle.Utils.Tests;

[TestClass]
public sealed class UeSnapshotSerializationTests
{
    [TestMethod]
    public void Serialize_UsesCamelCaseAndNoPointerFields()
    {
        var snapshot = CreateSnapshot("model-root", "model-child");
        snapshot.Models[0].Materials.Add(new UeMaterialSnapshot
        {
            ResourceId = "sha256:material",
            ShaderPackageResourceId = "sha256:shpk",
            ColorTable = System.Text.Json.JsonSerializer.SerializeToElement(new { rows = 32 })
        });

        var json = UeExportJson.Serialize(snapshot);

        StringAssert.Contains(json, "\"schemaVersion\"");
        StringAssert.Contains(json, "\"models\"");
        StringAssert.Contains(json, "\"attaches\"");
        Assert.IsFalse(json.Contains("captureStatus", StringComparison.Ordinal));
        Assert.IsFalse(json.Contains("ModelAddress", StringComparison.Ordinal));
        Assert.IsFalse(json.Contains("IntPtr", StringComparison.Ordinal));
        Assert.IsFalse(json.Contains("\"nint\"", StringComparison.Ordinal));
    }

    [TestMethod]
    public void CollectReferencedResourceIds_IncludesNestedAttachIds()
    {
        var snapshot = CreateSnapshot("model-root", "model-child");
        snapshot.Skeleton!.PartialSkeletons.Add(new UePartialSkeletonSnapshot
        {
            ResourceId = "sha256:sklb"
        });
        snapshot.Attach = new UeAttachSnapshot
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
        snapshot.Models[0].Materials.Add(new UeMaterialSnapshot
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
        snapshot.Models[0].Deformation = new UeDeformerSnapshot
        {
            PbdResourceId = "sha256:pbd"
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
        };

        var ids = UeExportValidator.CollectReferencedResourceIds(snapshot);

        Assert.AreEqual(0, ids.Count);
    }

    [TestMethod]
    public void EmptyPartialSkeleton_PreservesSlotMetadataAndNullResourceId()
    {
        var snapshot = new UeCharacterSnapshot
        {
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
        };

        using var document = System.Text.Json.JsonDocument.Parse(UeExportJson.Serialize(snapshot));
        var partial = document.RootElement
                              .GetProperty("skeleton")
                              .GetProperty("partialSkeletons")[0];

        Assert.AreEqual(3, partial.GetProperty("index").GetInt32());
        Assert.AreEqual(0x12u, partial.GetProperty("partialSkeletonFlagsRaw").GetUInt32());
        Assert.AreEqual(-1, partial.GetProperty("connectedBoneIndex").GetInt32());
        Assert.AreEqual(0u, partial.GetProperty("boneCount").GetUInt32());
        Assert.AreEqual(System.Text.Json.JsonValueKind.Null, partial.GetProperty("logicalPath").ValueKind);
        Assert.AreEqual(System.Text.Json.JsonValueKind.Null, partial.GetProperty("resolvedPath").ValueKind);
        Assert.AreEqual(System.Text.Json.JsonValueKind.Null, partial.GetProperty("resourceId").ValueKind);
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
            UeExportValidator.Validate(new UeCharacterSnapshot(), new UeResourceManifest(), tempDirectory);
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
            Models = [new UeModelSnapshot { ResourceId = modelReference.Id }],
            Skeleton = new UeSkeletonSnapshot
            {
                PartialSkeletons = [new UePartialSkeletonSnapshot { ResourceId = skeletonReference.Id }]
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
                new UeCharacterSnapshot { Models = [new UeModelSnapshot { ResourceId = reference.Id }] },
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
        };

        using var document = System.Text.Json.JsonDocument.Parse(UeExportJson.Serialize(snapshot));
        var value = document.RootElement
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
            Models =
            [
                new UeModelSnapshot
                {
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
        };

        using var document = System.Text.Json.JsonDocument.Parse(UeExportJson.Serialize(snapshot));
        var metadata = document.RootElement
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
            Models =
            [
                new UeModelSnapshot
                {
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
        };

        using var document = System.Text.Json.JsonDocument.Parse(UeExportJson.Serialize(snapshot));
        var overrides = document.RootElement
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
            Models =
            [
                new UeModelSnapshot
                {
                    Deformation = new UeDeformerSnapshot
                    {
                        Status = "Observed",
                        PbdResourceId = "sha256:pbd",
                        RaceSexId = 801,
                        DeformerId = 701
                    }
                }
            ]
        };

        var json = UeExportJson.Serialize(snapshot);
        using var document = System.Text.Json.JsonDocument.Parse(json);
        var deformation = document.RootElement.GetProperty("models")[0].GetProperty("deformation");

        Assert.AreEqual("Observed", deformation.GetProperty("status").GetString());
        Assert.AreEqual(801, deformation.GetProperty("raceSexId").GetInt32());
        Assert.AreEqual(701, deformation.GetProperty("deformerId").GetInt32());
        Assert.IsFalse(json.Contains("sourceRaceSexId", StringComparison.Ordinal));
        Assert.IsFalse(json.Contains("targetRaceSexId", StringComparison.Ordinal));
    }

    [TestMethod]
    public void AttachedCharacterNode_RetainsCustomizeAndModelMetadata()
    {
        var root = new UeCharacterSnapshot();
        root.Attaches.Add(new UeCharacterSnapshot
        {
            Capture = new UeCaptureSnapshot
            {
                CapturedAtUtc = DateTime.UtcNow,
                ModelType = "Weapon",
                GenderRace = 0
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
                    Id = "model-0",
                    SlotIndex = 0,
                    ModelType = "Weapon",
                    Enabled = true
                }
            ]
        });

        using var document = System.Text.Json.JsonDocument.Parse(UeExportJson.Serialize(root));
        var child = document.RootElement.GetProperty("attaches")[0];

        Assert.AreEqual("Weapon", child.GetProperty("capture").GetProperty("modelType").GetString());
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
            }
        };

        var json = UeExportJson.Serialize(snapshot);
        using var document = System.Text.Json.JsonDocument.Parse(json);
        var generator = document.RootElement.GetProperty("generator");

        Assert.AreEqual("Meddle-UE", generator.GetProperty("name").GetString());
        Assert.AreEqual("1.2.3", generator.GetProperty("version").GetString());
        Assert.AreEqual(System.Text.Json.JsonValueKind.Null, generator.GetProperty("commit").ValueKind);
        Assert.IsTrue(generator.GetProperty("dirty").GetBoolean());
        Assert.AreEqual("test-build-id", generator.GetProperty("buildId").GetString());
        Assert.IsFalse(json.Contains("compatibility", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void Schema_UsesCentralCurrentVersion()
    {
        Assert.AreEqual(UeExportSchema.CurrentVersion, new UeCharacterSnapshot().SchemaVersion);
        Assert.AreEqual(UeExportSchema.CurrentVersion, new UeResourceManifest().SchemaVersion);
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
        var child = childModelId == null
            ? null
            : new UeCharacterSnapshot
            {
                Models = [new UeModelSnapshot { ResourceId = childModelId }]
            };

        var root = new UeCharacterSnapshot
        {
            Models = [new UeModelSnapshot { ResourceId = rootModelId ?? "model-root" }]
        };
        if (child != null)
        {
            root.Attaches.Add(child);
        }

        return root;
    }
}
