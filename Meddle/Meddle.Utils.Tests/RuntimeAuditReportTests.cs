using System.Text.Json;
using Meddle.Utils.Audit;

namespace Meddle.Utils.Tests;

[TestClass]
public sealed class RuntimeAuditReportTests
{
    private static RuntimeAuditSession CreateRichSession()
    {
        var context = new RuntimeAuditCollectorContext(
            "0x1A2B",
            "Test Character",
            "Pc",
            0x1A2B,
            new DateTime(2026, 8, 21, 12, 0, 0, DateTimeKind.Utc));

        var collectors = new IRuntimeAuditCollector[]
        {
            new TestCollector(
                RuntimeAuditCategory.CharacterComposition,
                "GameObject",
                new Dictionary<string, object?>
                {
                    ["objectId"] = 12345u,
                    ["dataId"] = 0x20001u
                }),
            new TestCollector(
                RuntimeAuditCategory.CharacterComposition,
                "CharacterBase",
                new Dictionary<string, object?>
                {
                    ["modelType"] = "Human",
                    ["objectKind"] = "Pc",
                    ["modelId"] = 0x101u
                }),
            new TestCollector(
                RuntimeAuditCategory.CharacterComposition,
                "Model",
                new Dictionary<string, object?>
                {
                    ["slotIndex"] = 1u,
                    ["resource"] = "chara/human/c0101e0001/obj/body/b0001/model/c0101e0001_top.mdl",
                    ["logicalPath"] = "chara/human/c0101e0001/obj/body/b0001/model/c0101e0001_top.mdl",
                    ["enabled"] = true,
                    ["variant"] = 1,
                    ["relation"] = "unknown"
                }),
            new TestCollector(
                RuntimeAuditCategory.CharacterComposition,
                "ModelResource",
                new Dictionary<string, object?>
                {
                    ["models"] = new object?[]
                    {
                        new Dictionary<string, object?>
                        {
                            ["slotIndex"] = 1u,
                            ["resource"] = "chara/human/c0101e0001/obj/body/b0001/model/c0101e0001_top.mdl",
                            ["meshCount"] = (ushort)4,
                            ["submeshCount"] = (ushort)6,
                            ["vertexDeclarationCount"] = (ushort)1,
                            ["materialCount"] = (ushort)2,
                            ["boneCount"] = (ushort)24,
                            ["shapeCount"] = (ushort)3,
                            ["headerAvailable"] = true
                        }
                    }
                }),
            new TestCollector(
                RuntimeAuditCategory.CharacterComposition,
                "DrawObject",
                new Dictionary<string, object?>
                {
                    ["models"] = new object?[]
                    {
                        new Dictionary<string, object?>
                        {
                            ["slotIndex"] = 1u,
                            ["resource"] = "chara/human/c0101e0001/obj/body/b0001/model/c0101e0001_top.mdl",
                            ["logicalPath"] = "chara/human/c0101e0001/obj/body/b0001/model/c0101e0001_top.mdl",
                            ["enabled"] = true,
                            ["variant"] = 1,
                            ["relation"] = "unknown"
                        }
                    },
                    ["attach"] = new Dictionary<string, object?>
                    {
                        ["executeType"] = 1,
                        ["attachmentCount"] = 2,
                        ["partialSkeletonIdx"] = 3,
                        ["boneIdx"] = 4
                    }
                }),
            new TestCollector(
                RuntimeAuditCategory.SkeletonEvidence,
                "Skeleton",
                new Dictionary<string, object?>
                {
                    ["partialSkeletonCount"] = 1,
                    ["skeletonAddress"] = "0x1234",
                    ["partialSkeletons"] = new object?[]
                    {
                        new Dictionary<string, object?>
                        {
                            ["partialSkeletonIndex"] = 0,
                            ["handle"] = "chara/human/c0101e0001/skeleton/base/b0001/skl_c0101e0001_b0001.sklb",
                            ["resourceIdentityAvailable"] = true,
                            ["boneCount"] = 100,
                            ["connectedBoneIndex"] = 0,
                            ["poseCount"] = 1,
                            ["hierarchyAvailable"] = true,
                            ["parentRelationship"] = "available",
                            ["boneNames"] = new object?[] { "n_root", "n_hips", "n_waist" },
                            ["parentIndices"] = new object?[] { -1, 0, 1 }
                        }
                    },
                    ["attachRelationships"] = new Dictionary<string, object?>
                    {
                        ["attachmentCount"] = 2,
                        ["executeType"] = 1
                    }
                }),
            new TestCollector(
                RuntimeAuditCategory.Material,
                "Material",
                new Dictionary<string, object?>
                {
                    ["materials"] = new object?[]
                    {
                        new Dictionary<string, object?>
                        {
                            ["materialIndex"] = 0,
                            ["modelSlot"] = 1u,
                            ["path"] = "chara/human/c0101e0001/obj/body/b0001/vmat/c0101e0001_top_b0001.mtrl",
                            ["shaderPackage"] = "character.shpk",
                            ["hasColorTable"] = true,
                            ["textureCount"] = 3,
                            ["parameterBufferFloatCount"] = 16
                        }
                    }
                }),
            new TestCollector(
                RuntimeAuditCategory.RuntimeTexture,
                "TextureResourceHandle",
                new Dictionary<string, object?>
                {
                    ["bindings"] = new object?[]
                    {
                        new Dictionary<string, object?>
                        {
                            ["modelSlot"] = 1u,
                            ["materialIndex"] = 0,
                            ["index"] = 0,
                            ["bindingId"] = 0u,
                            ["sourcePath"] = "chara/human/c0101e0001/obj/body/b0001/tex/c0101e0001_top_b0001_d.tex",
                            ["resourcePath"] = "chara/human/c0101e0001/obj/body/b0001/tex/c0101e0001_top_b0001_d.tex",
                            ["generatedStatus"] = "file",
                            ["width"] = 512,
                            ["height"] = 512,
                            ["format"] = "BC7"
                        }
                    }
                }),
            new TestCollector(
                RuntimeAuditCategory.Customize,
                "Human",
                new Dictionary<string, object?>
                {
                    ["raceSexId"] = 101,
                    ["customize"] = new Dictionary<string, object?>
                    {
                        ["race"] = 1,
                        ["sex"] = 0,
                        ["tribe"] = 1,
                        ["face"] = 1,
                        ["hairstyle"] = 5,
                        ["skinColor"] = 100,
                        ["hairColor"] = 200
                    },
                    ["equipmentModelIds"] = new object?[]
                    {
                        new Dictionary<string, object?>
                        {
                            ["slot"] = 1,
                            ["slotName"] = "Body",
                            ["id"] = 101,
                            ["variant"] = 1,
                            ["stain0"] = 2,
                            ["stain1"] = 0
                        }
                    }
                }),
            new TestCollector(
                RuntimeAuditCategory.EquipmentVisibility,
                "EquipmentVisibility",
                new Dictionary<string, object?>
                {
                    ["slots"] = new object?[]
                    {
                        new Dictionary<string, object?>
                        {
                            ["slot"] = 0, ["slotName"] = "Head", ["resource"] = "head.mdl",
                            ["equipped"] = true, ["visible"] = false, ["hidden"] = true,
                            ["flags"] = 0u, ["reason"] = null
                        }
                    }
                }),
            new TestCollector(
                RuntimeAuditCategory.VisorState,
                "VisorState",
                new Dictionary<string, object?>
                {
                    ["visorSupported"] = null,
                    ["visorState"] = "toggled",
                    ["visorToggled"] = true,
                    ["affectedSlots"] = Array.Empty<object?>()
                }),
            new TestCollector(
                RuntimeAuditCategory.Attachment,
                "Attachment",
                new Dictionary<string, object?>
                {
                    ["attachments"] = new object?[]
                    {
                        new Dictionary<string, object?>
                        {
                            ["parent"] = "0x1A2B", ["child"] = "0x3000",
                            ["socket"] = null, ["transform"] = null
                        }
                    }
                }),
            new TestCollector(
                RuntimeAuditCategory.Weapon,
                "Weapon",
                new Dictionary<string, object?>
                {
                    ["weapons"] = new object?[]
                    {
                        new Dictionary<string, object?>
                        {
                            ["hand"] = "mainHand", ["visible"] = true,
                            ["resource"] = "weapon.mdl", ["model"] = null,
                            ["material"] = null, ["drawObject"] = "0x3000"
                        },
                        new Dictionary<string, object?>
                        {
                            ["hand"] = "offHand", ["visible"] = false,
                            ["resource"] = null, ["model"] = null,
                            ["material"] = null, ["drawObject"] = null
                        }
                    }
                }),
            new TestCollector(
                RuntimeAuditCategory.Deformer,
                "Deformer",
                new Dictionary<string, object?>
                {
                    ["slots"] = new object?[]
                    {
                        new Dictionary<string, object?>
                        {
                            ["slotIndex"] = 1u,
                            ["state"] = "unknown",
                            ["reason"] = "no deformer observation cached for this slot"
                        }
                    },
                    ["hookCacheEntryCount"] = 0
                },
                RuntimeAuditAvailability.Unknown,
                "deformer hook not fired for this character")
        };

        return RuntimeAuditRunner.RunAll("rich", context, collectors);
    }

    [TestMethod]
    public void Build_ProducesRequestedTopLevelSections()
    {
        var report = RuntimeAuditReport.Build(CreateRichSession());

        foreach (var section in new[]
                 {
                     "actor", "customize", "drawObjects", "meshes", "materials",
                     "textures", "equipment", "attachments", "skeleton",
                     "penumbraResolution", "appearanceState", "missingData", "verdict"
                 })
        {
            Assert.IsTrue(report.ContainsKey(section), $"missing report section {section}");
        }
    }

    [TestMethod]
    public void Build_ActorIdentity_PopulatesObjectAndModelIds()
    {
        var report = RuntimeAuditReport.Build(CreateRichSession());
        var actor = (Dictionary<string, object?>)report["actor"]!;

        Assert.AreEqual("Pc", actor["objectKind"]);
        Assert.AreEqual(12345u, actor["objectId"]);
        Assert.AreEqual(0x20001u, actor["dataId"]);
        Assert.AreEqual(0x101u, actor["modelId"]);
        Assert.AreEqual(1, actor["race"]);
        Assert.AreEqual(0, actor["gender"]);
        Assert.AreEqual(1, actor["tribe"]);
    }

    [TestMethod]
    public void Build_MeshEvidence_ReportsHeaderCountsAndUnknownPerMeshData()
    {
        var report = RuntimeAuditReport.Build(CreateRichSession());
        var meshes = (List<object?>)report["meshes"]!;
        Assert.IsTrue(meshes.Count > 0, $"expected mesh evidence, got {meshes.Count}");

        var mesh = meshes.OfType<Dictionary<string, object?>>()
                         .Single(m => m.TryGetValue("meshDataAvailable", out var available) &&
                                      available is true);
        Assert.AreEqual((ushort)4, mesh["meshCount"]);
        Assert.AreEqual((ushort)6, mesh["submeshCount"]);
        Assert.AreEqual((ushort)1, mesh["vertexDeclarationCount"]);
        Assert.AreEqual(true, mesh["meshDataAvailable"]);
        Assert.IsNull(mesh["vertexCount"]);
        Assert.IsNull(mesh["indexCount"]);
        StringAssert.Contains((string)mesh["reason"]!, "MDL parsing");
    }

    [TestMethod]
    public void Build_Equipment_MatchesFinalModelAndDyeToSlot()
    {
        var report = RuntimeAuditReport.Build(CreateRichSession());
        var equipment = (List<object?>)report["equipment"]!;

        Assert.AreEqual(1, equipment.Count, "expected exactly one equipment entry");
        var only = equipment.OfType<Dictionary<string, object?>>().Single();
        Assert.AreEqual(1, only["slot"]);
        Assert.AreEqual("Body", only["slotName"]);

        var body = equipment.OfType<Dictionary<string, object?>>()
                            .Single(e => e.TryGetValue("slotName", out var name) &&
                                         Equals(name, "Body"));
        Assert.AreEqual(101, body["modelId"]);
        Assert.AreEqual(2, body["stain0"]);
        StringAssert.Contains(
            (string)body["finalModel"]!,
            "c0101e0001_top.mdl");
        Assert.AreEqual(true, body["resolved"]);
        var finalMaterial = body["finalMaterial"] as List<object?>;
        Assert.IsNotNull(finalMaterial);
        Assert.AreEqual(1, finalMaterial!.Count);
    }

    [TestMethod]
    public void Build_Skeleton_ReportsCompletenessAndBoneEvidence()
    {
        var report = RuntimeAuditReport.Build(CreateRichSession());
        var skeleton = (Dictionary<string, object?>)report["skeleton"]!;

        Assert.AreEqual("COMPLETE", skeleton["completeness"]);
        var partials = (List<object?>)skeleton["partialSkeletons"]!;
        var partial = partials.OfType<Dictionary<string, object?>>().Single();
        Assert.AreEqual(100, partial["boneCount"]);
    }

    [TestMethod]
    public void Build_Penumbra_FlagsPathDifferenceWhenOverrideObserved()
    {
        var context = new RuntimeAuditCollectorContext(
            "0x1",
            "X",
            "Pc",
            1,
            DateTime.UtcNow);
        var session = RuntimeAuditRunner.RunAll(
            "penumbra",
            context,
            new IRuntimeAuditCollector[]
            {
                new TestCollector(
                    RuntimeAuditCategory.CharacterComposition,
                    "DrawObject",
                    new Dictionary<string, object?>
                    {
                        ["models"] = new object?[]
                        {
                            new Dictionary<string, object?>
                            {
                                ["slotIndex"] = 1u,
                                ["resource"] = "F:/Penumbra/mods/MyMod/body/b0001/model/c0101e0001_top.mdl",
                                ["logicalPath"] = "chara/human/c0101e0001/obj/body/b0001/model/c0101e0001_top.mdl"
                            }
                        }
                    })
            });

        var report = RuntimeAuditReport.Build(session);
        var penumbra = (Dictionary<string, object?>)report["penumbraResolution"]!;
        Assert.AreEqual(true, penumbra["overrideObserved"]);
    }

    [TestMethod]
    public void Build_Penumbra_EqualPathsKeepOverrideAndProvenanceUnknown()
    {
        var context = new RuntimeAuditCollectorContext("0x1", "X", "Pc", 1, DateTime.UtcNow);
        var session = RuntimeAuditRunner.RunAll(
            "equal-paths",
            context,
            new IRuntimeAuditCollector[]
            {
                new TestCollector(
                    RuntimeAuditCategory.CharacterComposition,
                    "DrawObject",
                    new Dictionary<string, object?>
                    {
                        ["models"] = new object?[]
                        {
                            new Dictionary<string, object?>
                            {
                                ["slotIndex"] = 0u,
                                ["resource"] = "chara/a.mdl",
                                ["logicalPath"] = "chara/a.mdl"
                            }
                        }
                    })
            });

        var report = RuntimeAuditReport.Build(session);
        var penumbra = (Dictionary<string, object?>)report["penumbraResolution"]!;
        var resource = ((List<object?>)penumbra["resources"]!)
            .OfType<Dictionary<string, object?>>()
            .Single();
        Assert.IsNull(resource["overridden"]);
        Assert.AreEqual("unknown", resource["provenance"]);

        var drawObjects = (List<object?>)report["drawObjects"]!;
        var model = drawObjects.OfType<Dictionary<string, object?>>().Single();
        Assert.IsNull(model["slotName"], "an unverified slot ordinal must not acquire semantic meaning");
    }

    [TestMethod]
    public void Build_AppearanceState_PreservesHiddenVisorHierarchyWeaponsAndUnknowns()
    {
        var report = RuntimeAuditReport.Build(CreateRichSession());
        var appearance = (Dictionary<string, object?>)report["appearanceState"]!;

        var visibility = (List<object?>)appearance["equipmentVisibility"]!;
        var head = visibility.OfType<Dictionary<string, object?>>().Single();
        Assert.AreEqual(true, head["equipped"]);
        Assert.AreEqual(false, head["visible"]);
        Assert.AreEqual(true, head["hidden"]);

        var visor = (Dictionary<string, object?>)appearance["visor"]!;
        Assert.AreEqual("toggled", visor["visorState"]);
        Assert.IsNull(visor["visorSupported"]);

        var hierarchy = (List<object?>)appearance["hierarchy"]!;
        Assert.IsTrue(hierarchy.OfType<Dictionary<string, object?>>().Any(n =>
            Equals(n["nodeId"], "0x3000") && Equals(n["parentId"], "0x1A2B")));

        var weapons = (List<object?>)appearance["weapons"]!;
        Assert.AreEqual(2, weapons.Count);
        var offHand = weapons.OfType<Dictionary<string, object?>>().Single(w => Equals(w["hand"], "offHand"));
        Assert.IsNull(offHand["resource"]);
        Assert.IsNull(offHand["model"]);
        Assert.IsNull(offHand["material"]);
    }

    [TestMethod]
    public void Build_MissingData_IncludesUnavailableReasonsAndKnownGaps()
    {
        var report = RuntimeAuditReport.Build(CreateRichSession());
        var missing = (List<object?>)report["missingData"]!;
        var entries = missing.OfType<Dictionary<string, object?>>().ToArray();

        Assert.IsTrue(entries.Any(
            e => Equals(e["sourceType"], "Deformer")));
        Assert.IsFalse(entries.Any(e => Equals(e["sourceType"], "Weapon")),
            "captured weapon state must not be reported as missing");
        Assert.IsTrue(entries.Any(e => Equals(e["sourceType"], "Mount")));
        Assert.IsTrue(entries.Any(e => Equals(e["sourceType"], "Ornament")));

        var verdict = (Dictionary<string, object?>)report["verdict"]!;
        Assert.AreEqual(false, verdict["reconstructionReady"]);
    }

    [TestMethod]
    public void Build_SerializesAsValidJson()
    {
        var report = RuntimeAuditReport.Build(CreateRichSession());
        var json = JsonSerializer.Serialize(report, RuntimeAuditSerializer.JsonOptions);
        using var document = JsonDocument.Parse(json);
        Assert.AreEqual(
            RuntimeAuditFormat.FormatMarker,
            document.RootElement.GetProperty("format").GetString());
        Assert.IsTrue(document.RootElement.TryGetProperty("equipment", out _));
    }

    private sealed class TestCollector : IRuntimeAuditCollector
    {
        private readonly string sourceType;
        private readonly object? value;
        private readonly RuntimeAuditAvailability availability;
        private readonly string? error;

        public TestCollector(
            RuntimeAuditCategory category,
            string sourceType,
            object? value,
            RuntimeAuditAvailability availability = RuntimeAuditAvailability.Available,
            string? error = null)
        {
            Category = category;
            this.sourceType = sourceType;
            this.value = value;
            this.availability = availability;
            this.error = error;
        }

        public RuntimeAuditCategory Category { get; }

        public RuntimeAuditCollectorResult Collect(RuntimeAuditCollectorContext context)
        {
            return new RuntimeAuditCollectorResult
            {
                Status = availability,
                Error = error,
                Observations =
                {
                    new RuntimeAuditObservation
                    {
                        Time = context.CapturedAtUtc.ToString("O"),
                        ActorId = context.ActorId,
                        Category = Category.ToString(),
                        SourceType = sourceType,
                        Value = value,
                        Availability = availability,
                        Error = error
                    }
                }
            };
        }
    }
}
