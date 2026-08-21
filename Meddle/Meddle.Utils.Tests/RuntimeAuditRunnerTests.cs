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
