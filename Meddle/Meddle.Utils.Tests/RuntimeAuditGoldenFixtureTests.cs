using System.Text.Json;
using Meddle.Utils.Audit;

namespace Meddle.Utils.Tests;

[TestClass]
public sealed class RuntimeAuditGoldenFixtureTests
{
    private static readonly DateTime GoldenTimestamp =
        new(2026, 8, 22, 1, 2, 3, DateTimeKind.Utc);

    [TestMethod]
    public async Task GoldenAuditDump_IsWrittenByRuntimeAuditSerializer()
    {
        var output = Environment.GetEnvironmentVariable("MEDDLE_AUDIT_GOLDEN_OUTPUT");
        var temporary = string.IsNullOrWhiteSpace(output);
        output ??= Path.Combine(Path.GetTempPath(), $"meddle-audit-golden-{Guid.NewGuid():N}");
        try
        {
            await RuntimeAuditSerializer.WriteAsync(
                CreateSession(),
                output,
                generatedAtUtc: GoldenTimestamp);
            using var runtime = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(output, RuntimeAuditFormat.RuntimeJson)));
            Assert.AreEqual(RuntimeAuditFormat.FormatMarker, runtime.RootElement.GetProperty("format").GetString());
            Assert.AreEqual(1, runtime.RootElement.GetProperty("schemaVersion").GetInt32());
            Assert.IsTrue(runtime.RootElement.GetProperty("observations").GetArrayLength() >= 5);
            using var projection = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(output, RuntimeAuditFormat.MaterialsJson)));
            Assert.AreEqual(JsonValueKind.Array, projection.RootElement.GetProperty("observations").ValueKind);
            Assert.AreEqual(
                GoldenTimestamp.ToString("O"),
                projection.RootElement.GetProperty("generatedAtUtc").GetString());
        }
        finally
        {
            if (temporary && Directory.Exists(output)) Directory.Delete(output, true);
        }
    }

    private static RuntimeAuditSession CreateSession()
    {
        var captured = GoldenTimestamp;
        RuntimeAuditObservation O(RuntimeAuditCategory category, string source, object? value,
            RuntimeAuditAvailability availability = RuntimeAuditAvailability.Available, string? error = null) => new()
        {
            Time = captured.ToString("O"), ActorId = "0xA11D17", Category = category.ToString(), SourceType = source,
            Value = value, Availability = availability, Error = error, CaptureId = "audit-golden"
        };

        return new RuntimeAuditSession
        {
            CaptureId = "audit-golden", CapturedAtUtc = captured, ActorId = "0xA11D17",
            ActorName = "Audit Golden", ObjectKind = "Pc", Generator = "Meddle runtime audit test",
            CollectorStatus = new Dictionary<string, RuntimeAuditAvailability>
            {
                [RuntimeAuditCategory.CharacterComposition.ToString()] = RuntimeAuditAvailability.Available,
                [RuntimeAuditCategory.Material.ToString()] = RuntimeAuditAvailability.Available,
                [RuntimeAuditCategory.RuntimeTexture.ToString()] = RuntimeAuditAvailability.Available,
                [RuntimeAuditCategory.SkeletonEvidence.ToString()] = RuntimeAuditAvailability.Available,
                [RuntimeAuditCategory.Deformer.ToString()] = RuntimeAuditAvailability.Unknown
            },
            Observations =
            [
                O(RuntimeAuditCategory.CharacterComposition, "DrawObject", new Dictionary<string, object?>
                {
                    ["models"] = new object?[]
                    {
                        new Dictionary<string, object?>
                        {
                            ["slotIndex"] = 3u, ["resource"] = "mods/body.mdl", ["logicalPath"] = "chara/body.mdl",
                            ["enabled"] = true, ["variant"] = 2
                        }
                    }
                }),
                O(RuntimeAuditCategory.Material, "Material", new Dictionary<string, object?>
                {
                    ["materials"] = new object?[]
                    {
                        new Dictionary<string, object?>
                        {
                            ["modelSlot"] = 3u, ["materialIndex"] = 0, ["path"] = "mods/body.mtrl",
                            ["logicalPath"] = "chara/body.mtrl", ["shaderPackage"] = "shader/sm5.shpk"
                        }
                    }
                }),
                O(RuntimeAuditCategory.RuntimeTexture, "TextureResourceHandle", new Dictionary<string, object?>
                {
                    ["bindings"] = new object?[]
                    {
                        new Dictionary<string, object?>
                        {
                            ["modelSlot"] = 3u, ["materialIndex"] = 0, ["index"] = 0,
                            ["runtimeHandle"] = "0xBEEF", ["sourcePath"] = "chara/body.tex", ["resourcePath"] = "mods/body.tex",
                            ["generatedStatus"] = "file", ["generatedReason"] = "a runtime resource path was observed"
                        }
                    }
                }),
                O(RuntimeAuditCategory.SkeletonEvidence, "Skeleton", new Dictionary<string, object?>
                {
                    ["partialSkeletonCount"] = 2,
                    ["partialSkeletons"] = new object?[]
                    {
                        new Dictionary<string, object?> { ["partialSkeletonIndex"] = 0, ["handle"] = "chara/base.sklb", ["boneCount"] = 120 },
                        new Dictionary<string, object?> { ["partialSkeletonIndex"] = 1, ["handle"] = "chara/extra.sklb", ["boneCount"] = 12 }
                    }
                }),
                O(RuntimeAuditCategory.Deformer, "Deformer", new Dictionary<string, object?>
                {
                    ["slots"] = new object?[]
                    {
                        new Dictionary<string, object?> { ["slotIndex"] = 3u, ["state"] = "unknown", ["reason"] = "no cached hook evidence" }
                    }
                }, RuntimeAuditAvailability.Unknown, "deformer evidence is incomplete")
            ]
        };
    }
}
