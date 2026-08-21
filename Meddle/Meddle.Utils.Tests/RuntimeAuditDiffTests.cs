using System.Text.Json;
using Meddle.Utils.Audit;

namespace Meddle.Utils.Tests;

[TestClass]
public sealed class RuntimeAuditDiffTests
{
    private static RuntimeAuditSession CreateSession(
        string captureId,
        int? height,
        string[]? modelPaths = null,
        string[]? skeletonHandles = null,
        string[]? texturePaths = null)
    {
        var context = new RuntimeAuditCollectorContext(
            "0x1234",
            "Test Character",
            "Pc",
            0x1234,
            new DateTime(2026, 8, 21, 12, 0, 0, DateTimeKind.Utc));

        var collectors = new List<IRuntimeAuditCollector>
        {
            new ResourceCollector(
                RuntimeAuditCategory.CharacterComposition,
                "DrawObject",
                "logicalPath",
                modelPaths ?? []),
            new ResourceCollector(
                RuntimeAuditCategory.SkeletonEvidence,
                "PartialSkeleton",
                "handle",
                skeletonHandles ?? []),
            new ResourceCollector(
                RuntimeAuditCategory.RuntimeTexture,
                "TextureResourceHandle",
                "sourcePath",
                texturePaths ?? []),
            new TestCustomizeCollector(height)
        };
        return RuntimeAuditRunner.RunAll(captureId, context, collectors);
    }

    [TestMethod]
    public void Diff_IdenticalCaptures_IsEmpty()
    {
        var before = CreateSession("A", height: 50, modelPaths: ["c0101e0001_top"]);
        var after = CreateSession("B", height: 50, modelPaths: ["c0101e0001_top"]);

        var diff = RuntimeAuditDiff.Diff(before, after);

        Assert.IsTrue(diff.IsEmpty);
    }

    [TestMethod]
    public void Diff_AddedResource_Detected()
    {
        var before = CreateSession("A", height: 50, modelPaths: []);
        var after = CreateSession("B", height: 50, modelPaths: ["c0101e0001_top"]);

        var diff = RuntimeAuditDiff.Diff(before, after);

        Assert.AreEqual(1, diff.Added.Count);
        Assert.AreEqual(0, diff.Removed.Count);
        Assert.AreEqual(0, diff.Changed.Count);
        Assert.AreEqual("c0101e0001_top", diff.Added[0].Identity);
        Assert.AreEqual(RuntimeAuditDiffChange.Added, diff.Added[0].Change);
    }

    [TestMethod]
    public void Diff_RemovedResource_Detected()
    {
        var before = CreateSession("A", height: 50, skeletonHandles: ["chara/x/skl/skl_c0101e0001.sklb"]);
        var after = CreateSession("B", height: 50, skeletonHandles: []);

        var diff = RuntimeAuditDiff.Diff(before, after);

        Assert.AreEqual(1, diff.Removed.Count);
        Assert.AreEqual("chara/x/skl/skl_c0101e0001.sklb", diff.Removed[0].Identity);
    }

    [TestMethod]
    public void Diff_ChangedParameter_Detected()
    {
        var before = CreateSession("A", height: 50);
        var after = CreateSession("B", height: 75);

        var diff = RuntimeAuditDiff.Diff(before, after);

        Assert.AreEqual(1, diff.Changed.Count);
        Assert.AreEqual("height", diff.Changed[0].Identity);
        Assert.AreEqual(50, ((JsonElement)JsonSerializer.SerializeToElement(diff.Changed[0].BeforeValue)).GetInt32());
        Assert.AreEqual(75, ((JsonElement)JsonSerializer.SerializeToElement(diff.Changed[0].AfterValue)).GetInt32());
    }

    [TestMethod]
    public void Diff_ChangedTextureBinding_Detected()
    {
        var before = CreateSession("A", height: 50, texturePaths: ["chara/common/texture/skin.tex"]);
        var after = CreateSession("B", height: 50, texturePaths: ["chara/common/texture/skin2.tex"]);

        var diff = RuntimeAuditDiff.Diff(before, after);

        Assert.AreEqual(1, diff.Added.Count);
        Assert.AreEqual(1, diff.Removed.Count);
        Assert.AreEqual("chara/common/texture/skin2.tex", diff.Added[0].Identity);
        Assert.AreEqual("chara/common/texture/skin.tex", diff.Removed[0].Identity);
    }

    [TestMethod]
    public void Diff_IsDeterministic()
    {
        var before = CreateSession(
            "A",
            height: 50,
            modelPaths: ["b", "a", "c"],
            skeletonHandles: ["sklb-b", "sklb-a"]);
        var after = CreateSession(
            "B",
            height: 75,
            modelPaths: ["c", "b", "d"],
            skeletonHandles: ["sklb-b"]);

        var first = RuntimeAuditDiff.Diff(before, after);
        var second = RuntimeAuditDiff.Diff(before, after);

        var firstJson = JsonSerializer.Serialize(first, RuntimeAuditSerializer.JsonOptions);
        var secondJson = JsonSerializer.Serialize(second, RuntimeAuditSerializer.JsonOptions);
        Assert.AreEqual(firstJson, secondJson);

        // Ordering must be stable and sorted so humans can rely on it.
        var identities = first.Added.Select(e => e.Identity).ToArray();
        Assert.IsTrue(
            identities.SequenceEqual(identities.OrderBy(i => i, StringComparer.Ordinal)),
            "Added entries must be sorted by identity");
    }

    [TestMethod]
    public void Diff_MissingData_DoesNotThrow()
    {
        var context = new RuntimeAuditCollectorContext(
            "0x1",
            "X",
            "Pc",
            1,
            DateTime.UtcNow);
        var before = RuntimeAuditRunner.RunAll(
            "A",
            context,
            new IRuntimeAuditCollector[]
            {
                new ResourceCollector(RuntimeAuditCategory.CharacterComposition, "DrawObject", "logicalPath", ["gone"]),
                new ResourceCollector(RuntimeAuditCategory.Material, "Material", "path", null)
            });
        var after = RuntimeAuditRunner.RunAll(
            "B",
            context,
            new IRuntimeAuditCollector[] { new ResourceCollector(RuntimeAuditCategory.CharacterComposition, "DrawObject", "logicalPath", []) });

        var diff = RuntimeAuditDiff.Diff(before, after);

        Assert.AreEqual(1, diff.Removed.Count);
        Assert.IsTrue(diff.Added.Count == 0 && diff.Changed.Count == 0);
    }

    private sealed class ResourceCollector : IRuntimeAuditCollector
    {
        private readonly string sourceType;
        private readonly string identityField;
        private readonly string[]? paths;

        public ResourceCollector(RuntimeAuditCategory category, string sourceType, string identityField, string[]? paths)
        {
            Category = category;
            this.sourceType = sourceType;
            this.identityField = identityField;
            this.paths = paths;
        }

        public RuntimeAuditCategory Category { get; }

        public RuntimeAuditCollectorResult Collect(RuntimeAuditCollectorContext context)
        {
            var observations = new List<RuntimeAuditObservation>();
            if (paths != null)
            {
                foreach (var path in paths)
                {
                    observations.Add(new RuntimeAuditObservation
                    {
                        Time = context.CapturedAtUtc.ToString("O"),
                        ActorId = context.ActorId,
                        Category = Category.ToString(),
                        SourceType = sourceType,
                        Value = new Dictionary<string, object?> { [identityField] = path },
                        Availability = RuntimeAuditAvailability.Available
                    });
                }
            }

            var result = new RuntimeAuditCollectorResult
            {
                Status = observations.Count > 0 ? RuntimeAuditAvailability.Available : RuntimeAuditAvailability.Unknown
            };
            result.Observations.AddRange(observations);
            return result;
        }
    }

    private sealed class TestCustomizeCollector : IRuntimeAuditCollector
    {
        private readonly int? height;

        public TestCustomizeCollector(int? height)
        {
            this.height = height;
        }

        public RuntimeAuditCategory Category => RuntimeAuditCategory.Customize;

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
                        SourceType = "Human",
                        Value = new Dictionary<string, object?>
                        {
                            ["height"] = height,
                            ["raceSexId"] = 801
                        },
                        Availability = RuntimeAuditAvailability.Available
                    }
                }
            };
        }
    }
}
