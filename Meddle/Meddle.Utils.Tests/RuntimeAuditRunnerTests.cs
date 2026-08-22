using Meddle.Utils.Audit;

namespace Meddle.Utils.Tests;

[TestClass]
public sealed class RuntimeAuditRunnerTests
{
    [TestMethod]
    public void FailingCollector_ProducesUnavailableObservation_AndContinues()
    {
        var context = new RuntimeAuditCollectorContext(
            "0x1",
            "X",
            "Pc",
            1,
            new DateTime(2026, 8, 21, 12, 0, 0, DateTimeKind.Utc));

        var session = RuntimeAuditRunner.RunAll(
            "A",
            context,
            new IRuntimeAuditCollector[]
            {
                new ThrowingCollector(RuntimeAuditCategory.Material),
                new StaticCollector(RuntimeAuditCategory.Customize, RuntimeAuditAvailability.Available)
            });

        Assert.AreEqual(RuntimeAuditAvailability.Unavailable, session.CollectorStatus["Material"]);
        Assert.AreEqual(RuntimeAuditAvailability.Available, session.CollectorStatus["Customize"]);
        Assert.AreEqual(2, session.Observations.Count);

        var failure = session.Observations.Single(o => o.Category == "Material");
        Assert.AreEqual(RuntimeAuditAvailability.Unavailable, failure.Availability);
        StringAssert.Contains(failure.Error, "boom");

        var success = session.Observations.Single(o => o.Category == "Customize");
        Assert.AreEqual(RuntimeAuditAvailability.Available, success.Availability);
    }

    [TestMethod]
    public void NullTarget_CollectorReturnsUnavailable_DoesNotThrow()
    {
        var context = new RuntimeAuditCollectorContext(
            "0x0",
            "None",
            "None",
            0,
            DateTime.UtcNow);

        var session = RuntimeAuditRunner.RunAll(
            "A",
            context,
            new IRuntimeAuditCollector[] { new StaticCollector(RuntimeAuditCategory.Deformer, RuntimeAuditAvailability.Unavailable) });

        Assert.AreEqual(RuntimeAuditAvailability.Unavailable, session.CollectorStatus["Deformer"]);
        Assert.AreEqual(1, session.Observations.Count);
        Assert.AreEqual(RuntimeAuditAvailability.Unavailable, session.Observations[0].Availability);
    }

    [TestMethod]
    public void CollectorStatusMerge_LaterAvailableEvidenceReplacesInitialUnknown()
    {
        Assert.AreEqual(
            RuntimeAuditAvailability.Available,
            RuntimeAuditAvailabilityRules.Merge(
                RuntimeAuditAvailability.Unknown,
                RuntimeAuditAvailability.Available));
        Assert.AreEqual(
            RuntimeAuditAvailability.Available,
            RuntimeAuditAvailabilityRules.Merge(
                RuntimeAuditAvailability.Unavailable,
                RuntimeAuditAvailability.Available));
        Assert.AreEqual(
            RuntimeAuditAvailability.Available,
            RuntimeAuditAvailabilityRules.Merge(
                RuntimeAuditAvailability.Available,
                RuntimeAuditAvailability.Unknown));
    }

    [TestMethod]
    public void RuntimeTextureWithoutResourcePath_IsAmbiguousNotGenerated()
    {
        var evidence = RuntimeTextureEvidence.ClassifyGeneratedStatus(
            "chara/common/texture/decal.tex",
            null,
            hasExplicitRuntimeGeneratedEvidence: false);

        Assert.AreEqual("unknown", evidence.Status);
        StringAssert.Contains(evidence.Reason, "resource path");
    }

    [TestMethod]
    public void WeaponMaterialAndTextureTraversal_IncludesExplicitChildDrawObjects()
    {
        var roots = RuntimeMaterialRootTraversal.Build(
            (nint)0x1000,
            [
                new RuntimeWeaponDrawObjectEvidence((nint)0x2000, 0, true),
                new RuntimeWeaponDrawObjectEvidence((nint)0x3000, 1, true),
                new RuntimeWeaponDrawObjectEvidence((nint)0x4000, 2, false),
                new RuntimeWeaponDrawObjectEvidence((nint)0x2000, 3, true)
            ]);

        Assert.AreEqual(3, roots.Count);
        Assert.AreEqual("Actor", roots[0].OwnerKind);
        CollectionAssert.AreEqual(
            new[] { 0, 1 },
            roots.Where(root => root.OwnerKind == "Weapon")
                .Select(root => root.WeaponIndex!.Value)
                .ToArray());
    }

    private sealed class ThrowingCollector : IRuntimeAuditCollector
    {
        public ThrowingCollector(RuntimeAuditCategory category)
        {
            Category = category;
        }

        public RuntimeAuditCategory Category { get; }

        public RuntimeAuditCollectorResult Collect(RuntimeAuditCollectorContext context)
        {
            throw new InvalidOperationException("boom");
        }
    }

    private sealed class StaticCollector : IRuntimeAuditCollector
    {
        private readonly RuntimeAuditAvailability availability;

        public StaticCollector(RuntimeAuditCategory category, RuntimeAuditAvailability availability)
        {
            Category = category;
            this.availability = availability;
        }

        public RuntimeAuditCategory Category { get; }

        public RuntimeAuditCollectorResult Collect(RuntimeAuditCollectorContext context)
        {
            return new RuntimeAuditCollectorResult
            {
                Status = availability,
                Observations =
                {
                    new RuntimeAuditObservation
                    {
                        Time = context.CapturedAtUtc.ToString("O"),
                        ActorId = context.ActorId,
                        Category = Category.ToString(),
                        SourceType = "test",
                        Value = "value",
                        Availability = availability
                    }
                }
            };
        }
    }
}
