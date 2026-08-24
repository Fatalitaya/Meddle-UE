using System.Text.Json;
using Meddle.Utils.Audit;

namespace Meddle.Utils.Tests;

[TestClass]
public sealed class AnimationEvidenceTests
{
    [TestMethod]
    public void Availability_BothHooksAvailable_IsAvailable()
    {
        Assert.AreEqual(
            RuntimeAuditAvailability.Available,
            AnimationEvidenceAvailabilityPolicy.Evaluate(
                characterBaseAvailable: true,
                syncHookAvailable: true,
                asyncHookAvailable: true));
    }

    [TestMethod]
    public void Availability_OnlyOneHook_IsUnknown()
    {
        Assert.AreEqual(
            RuntimeAuditAvailability.Unknown,
            AnimationEvidenceAvailabilityPolicy.Evaluate(
                characterBaseAvailable: true,
                syncHookAvailable: true,
                asyncHookAvailable: false));
        Assert.AreEqual(
            RuntimeAuditAvailability.Unknown,
            AnimationEvidenceAvailabilityPolicy.Evaluate(
                characterBaseAvailable: true,
                syncHookAvailable: false,
                asyncHookAvailable: true));
    }

    [TestMethod]
    public void Availability_NoHooks_IsUnknown()
    {
        Assert.AreEqual(
            RuntimeAuditAvailability.Unknown,
            AnimationEvidenceAvailabilityPolicy.Evaluate(
                characterBaseAvailable: true,
                syncHookAvailable: false,
                asyncHookAvailable: false));
    }

    [TestMethod]
    public void Availability_NoCharacterBase_IsUnavailableEvenWithHooks()
    {
        Assert.AreEqual(
            RuntimeAuditAvailability.Unavailable,
            AnimationEvidenceAvailabilityPolicy.Evaluate(
                characterBaseAvailable: false,
                syncHookAvailable: true,
                asyncHookAvailable: true));
    }

    [TestMethod]
    public async Task SerializeRoundTrip_PreservesAnimationEvidenceCategory()
    {
        var context = new RuntimeAuditCollectorContext(
            "0x1",
            "X",
            "Pc",
            1,
            new DateTime(2026, 8, 21, 12, 0, 0, DateTimeKind.Utc));
        var session = RuntimeAuditRunner.RunAll(
            "anim-evidence",
            context,
            new IRuntimeAuditCollector[]
            {
                new TestCollector(
                    RuntimeAuditCategory.AnimationEvidence,
                    "AnimationEvidence",
                    new Dictionary<string, object?>
                    {
                        ["partialSkeletons"] = new object?[]
                        {
                            new Dictionary<string, object?>
                            {
                                ["partialSkeletonIndex"] = 0,
                                ["handlePath"] = "chara/human/c0101/skeleton/base/b0001.sklb",
                                ["poseCount"] = 2
                            }
                        },
                        ["papHooks"] = new Dictionary<string, object?>
                        {
                            ["sync"] = new Dictionary<string, object?> { ["available"] = true },
                            ["async"] = new Dictionary<string, object?> { ["available"] = false, ["reason"] = "signature not found" }
                        },
                        ["recentPapLoads"] = new object?[]
                        {
                            new Dictionary<string, object?>
                            {
                                ["path"] = "chara/human/c0101/anim/bt/common/battle.pap",
                                ["source"] = "Sync",
                                ["seenCount"] = 3
                            }
                        },
                        ["papAttribution"] = "Process-level temporal evidence only; no actor attribution is implied."
                    })
            });

        var directory = Path.Combine(Path.GetTempPath(), $"meddle-audit-{Guid.NewGuid():N}");
        try
        {
            await RuntimeAuditSerializer.WriteAsync(session, directory);
            var loaded = await RuntimeAuditSerializer.LoadAsync(directory);

            Assert.IsNotNull(loaded);
            var observation = loaded!.Observations.Single();
            Assert.AreEqual(RuntimeAuditCategory.AnimationEvidence.ToString(), observation.Category);
            Assert.AreEqual(RuntimeAuditAvailability.Available, observation.Availability);
            var value = Assert.IsInstanceOfType<JsonElement>(observation.Value);
            Assert.IsTrue(value.TryGetProperty("recentPapLoads", out _));
            Assert.IsTrue(value.TryGetProperty("papAttribution", out _));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [TestMethod]
    public void Diff_AnimationEvidenceObservation_IsDetected()
    {
        var before = CreateSession("A", papPaths: []);
        var after = CreateSession("B", papPaths: ["chara/human/c0101/anim/bt/common/battle.pap"]);

        var diff = RuntimeAuditDiff.Diff(before, after);

        Assert.IsFalse(diff.IsEmpty);
        Assert.AreEqual(1, diff.Changed.Count);
        var changed = Assert.IsInstanceOfType<RuntimeAuditDiffEntry>(diff.Changed.Single());
        Assert.AreEqual(RuntimeAuditCategory.AnimationEvidence.ToString(), changed.Category);
        Assert.AreEqual(RuntimeAuditDiffChange.Changed, changed.Change);
    }

    private static RuntimeAuditSession CreateSession(string captureId, string[] papPaths)
    {
        var context = new RuntimeAuditCollectorContext(
            "0x1234",
            "Test Character",
            "Pc",
            0x1234,
            new DateTime(2026, 8, 21, 12, 0, 0, DateTimeKind.Utc));
        return RuntimeAuditRunner.RunAll(
            captureId,
            context,
            new IRuntimeAuditCollector[]
            {
                new TestCollector(
                    RuntimeAuditCategory.AnimationEvidence,
                    "AnimationEvidence",
                    new Dictionary<string, object?>
                    {
                        ["recentPapLoads"] = papPaths
                            .Select(path => (object)new Dictionary<string, object?>
                            {
                                ["path"] = path,
                                ["source"] = "Sync",
                                ["seenCount"] = 1
                            })
                            .ToList()
                    })
            });
    }

    private sealed class TestCollector : IRuntimeAuditCollector
    {
        private readonly object? value;

        public TestCollector(RuntimeAuditCategory category, string sourceType, object? value)
        {
            Category = category;
            SourceType = sourceType;
            this.value = value;
        }

        public RuntimeAuditCategory Category { get; }
        private string SourceType { get; }

        public RuntimeAuditCollectorResult Collect(RuntimeAuditCollectorContext context)
        {
            return new RuntimeAuditCollectorResult
            {
                Status = RuntimeAuditAvailability.Available,
                Observations =
                {
                    new RuntimeAuditObservation
                    {
                        Time = context.CapturedAtUtc.ToString("O"),
                        ActorId = context.ActorId,
                        Category = Category.ToString(),
                        SourceType = SourceType,
                        Value = value,
                        Availability = RuntimeAuditAvailability.Available
                    }
                }
            };
        }
    }
}
