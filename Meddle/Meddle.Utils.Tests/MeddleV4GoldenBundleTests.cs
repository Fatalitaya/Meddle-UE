using System.Text;
using System.Text.Json;
using System.Buffers.Binary;
using Meddle.Utils.Export.Ue;

namespace Meddle.Utils.Tests;

[TestClass]
public sealed class MeddleV4GoldenBundleTests
{
    [TestMethod]
    public void GoldenBundle_IsWrittenByProductionSerializerAndCompleter()
    {
        var output = Environment.GetEnvironmentVariable("MEDDLE_V4_GOLDEN_OUTPUT");
        var temporary = string.IsNullOrWhiteSpace(output);
        output ??= Path.Combine(Path.GetTempPath(), $"meddle-v4-golden-{Guid.NewGuid():N}");

        try
        {
            var (snapshot, bundle) = CreateGoldenBundle();
            UeExportCompleter.Complete(snapshot, bundle, output);

            using var character = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "character.json")));
            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "manifest.json")));
            Assert.AreEqual(4, character.RootElement.GetProperty("schemaVersion").GetInt32());
            Assert.AreEqual(4, manifest.RootElement.GetProperty("schemaVersion").GetInt32());
            Assert.AreEqual(1, character.RootElement.GetProperty("root").GetProperty("attaches").GetArrayLength());
            Assert.AreEqual(2, character.RootElement.GetProperty("root").GetProperty("skeleton").GetProperty("partialSkeletons").GetArrayLength());
            Assert.IsTrue(manifest.RootElement.GetProperty("resources").EnumerateArray().Any(
                x => x.GetProperty("origin").GetString() == "runtimegenerated"));
        }
        finally
        {
            if (temporary && Directory.Exists(output))
            {
                Directory.Delete(output, true);
            }
        }
    }

    private static (UeCharacterSnapshot Snapshot, UeResourceBundle Bundle) CreateGoldenBundle()
    {
        var builder = new UeResourceManifestBuilder();
        UeResourceReference Add(UeResourceType type, string logical, string resolved, string payload, UeResourceOrigin origin = UeResourceOrigin.Game) =>
            builder.Add(new UeResourceCandidate
            {
                Type = type,
                LogicalPath = logical,
                ResolvedPath = resolved,
                Data = Encoding.UTF8.GetBytes(payload),
                Origin = origin
            });
        UeResourceReference AddBytes(UeResourceType type, string logical, string resolved, byte[] payload) =>
            builder.Add(new UeResourceCandidate
            {
                Type = type,
                LogicalPath = logical,
                ResolvedPath = resolved,
                Data = payload,
                Origin = UeResourceOrigin.Game
            });

        var rootModel = Add(UeResourceType.Mdl, "chara/root.mdl", "mods/override/root.mdl", "golden-root-mdl", UeResourceOrigin.Modded);
        var childModel = Add(UeResourceType.Mdl, "chara/weapon.mdl", "chara/weapon.mdl", "golden-child-mdl");
        var rootMaterial = Add(UeResourceType.Mtrl, "chara/root.mtrl", "mods/override/root.mtrl", "golden-root-mtrl", UeResourceOrigin.Modded);
        var childMaterial = Add(UeResourceType.Mtrl, "chara/weapon.mtrl", "chara/weapon.mtrl", "golden-child-mtrl");
        var rootTexture = Add(UeResourceType.Tex, "chara/root.tex", "mods/override/root.tex", "golden-root-tex", UeResourceOrigin.Modded);
        var childTexture = Add(UeResourceType.Tex, "chara/weapon.tex", "chara/weapon.tex", "golden-child-tex");
        var shader = Add(UeResourceType.Shpk, "shader/sm5.shpk", "shader/sm5.shpk", "golden-shpk");
        var skeleton0 = AddBytes(UeResourceType.Sklb, "chara/base.sklb", "chara/base.sklb", ValidSklb(7));
        var skeleton1 = AddBytes(UeResourceType.Sklb, "chara/extra.sklb", "chara/extra.sklb", ValidSklb(8));
        var pbd = Add(UeResourceType.Pbd, "chara/human.pbd", "chara/human.pbd", "golden-pbd");
        var runtimePng = builder.Add(new UeResourceCandidate
        {
            Type = UeResourceType.Tex,
            LogicalPath = "runtime/decal.png",
            ResolvedPath = "runtime/decal.png",
            Data = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M/wHwAF/gL+XwW5WQAAAABJRU5ErkJggg=="),
            Origin = UeResourceOrigin.RuntimeGenerated,
            RuntimeTexture = new UeRuntimeTextureMetadata
            {
                Encoding = "png",
                Width = 1,
                Height = 1,
                Source = "decal",
                LogicalPath = "runtime/decal.png",
                ResolvedPath = "runtime/decal.png"
            }
        });
        builder.AddObservation("chara/missing.pap", "chara/missing.pap", UeResourceType.Pap,
            UeResourceOrigin.Game, UeResourceAvailability.Unavailable, "not resident during capture");
        builder.AddObservation("chara/ambiguous.tex", "chara/ambiguous.tex", UeResourceType.Tex,
            UeResourceOrigin.Unknown, UeResourceAvailability.Unresolved, "runtime identity could not be resolved");

        var root = new UeCharacterNodeSnapshot
        {
            NodeId = "root",
            Capture = new UeCaptureSnapshot
            {
                CapturedAtUtc = new DateTime(2026, 8, 22, 0, 0, 0, DateTimeKind.Utc),
                CharacterName = "Golden",
                ObjectKind = "Pc",
                ModelType = "Human",
                GenderRace = 101
            },
            Models =
            [
                new UeModelSnapshot
                {
                    Id = "root/model-0", SlotIndex = 0, ModelType = "Human", Enabled = true,
                    LogicalPath = "chara/root.mdl", ResolvedPath = "mods/override/root.mdl", ResourceId = rootModel.Id,
                    Materials =
                    [
                        new UeMaterialSnapshot
                        {
                            LogicalPath = "chara/root.mtrl", ResolvedPath = "mods/override/root.mtrl",
                            ResourceId = rootMaterial.Id, ShaderPackageResourceId = shader.Id,
                            Textures =
                            [
                                new UeTextureSnapshot { LogicalPath = "chara/root.tex", ResolvedPath = "mods/override/root.tex", ResourceId = rootTexture.Id },
                                new UeTextureSnapshot { LogicalPath = "runtime/decal.png", ResolvedPath = "runtime/decal.png", ResourceId = runtimePng.Id }
                            ]
                        }
                    ],
                    Deformation = new UeDeformerSnapshot
                    {
                        Status = UeDeformerStatus.Observed, PbdResourcePath = "chara/human.pbd", RaceSexId = 101, DeformerId = 1,
                        PbdResource = new UePbdResourceSnapshot { ResourceId = pbd.Id, Availability = UeResourceAvailability.Available }
                    }
                }
            ],
            Skeleton = new UeSkeletonSnapshot
            {
                PartialSkeletons =
                [
                    new UePartialSkeletonSnapshot { Index = 0, LogicalPath = "chara/base.sklb", ResolvedPath = "chara/base.sklb", ResourceId = skeleton0.Id, ConnectedBoneIndex = -1 },
                    new UePartialSkeletonSnapshot { Index = 1, LogicalPath = "chara/extra.sklb", ResolvedPath = "chara/extra.sklb", ResourceId = skeleton1.Id, ConnectedBoneIndex = 0 }
                ]
            }
        };
        root.Attaches.Add(new UeCharacterNodeSnapshot
        {
            NodeId = "root/attach-0",
            Capture = new UeCaptureSnapshot { CapturedAtUtc = root.Capture.CapturedAtUtc, ModelType = "Weapon" },
            Attach = new UeAttachSnapshot
            {
                AttachmentCount = 1,
                ExecuteType = 1,
                PartialSkeletonIdx = 0,
                BoneIdx = 0,
                OffsetTransform = new UeTransformSnapshot
                {
                    Translation = [1.0f, 2.0f, 3.0f],
                    Rotation = [0.0f, 0.0f, 0.0f, 1.0f],
                    Scale = [1.0f, 1.0f, 1.0f]
                }
            },
            Models =
            [
                new UeModelSnapshot
                {
                    Id = "root/attach-0/model-0", SlotIndex = 0, ModelType = "Weapon", Enabled = true,
                    LogicalPath = "chara/weapon.mdl", ResolvedPath = "chara/weapon.mdl", ResourceId = childModel.Id,
                    Materials =
                    [
                        new UeMaterialSnapshot
                        {
                            LogicalPath = "chara/weapon.mtrl", ResolvedPath = "chara/weapon.mtrl",
                            ResourceId = childMaterial.Id, ShaderPackageResourceId = shader.Id,
                            Textures = [new UeTextureSnapshot { LogicalPath = "chara/weapon.tex", ResolvedPath = "chara/weapon.tex", ResourceId = childTexture.Id }]
                        }
                    ]
                }
            ]
        });

        return (new UeCharacterSnapshot { Root = root }, builder.BuildBundle());
    }

    private static byte[] ValidSklb(int skeletonId)
    {
        const int headerSize = 40;
        var bytes = new byte[48];
        Encoding.ASCII.GetBytes("blks").CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 0x31333030);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), headerSize);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), headerSize);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), 0);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(20), skeletonId);
        for (var index = 0; index < 4; index++)
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(24 + index * 4), index + 1);
        for (var index = 0; index < 8; index++) bytes[40 + index] = (byte)(0x20 + index);
        return bytes;
    }
}
