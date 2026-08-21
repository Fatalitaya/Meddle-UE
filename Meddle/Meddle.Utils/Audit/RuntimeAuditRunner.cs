namespace Meddle.Utils.Audit;

/// <summary>
/// Runs a set of collectors against one context and builds a session.
///
/// The runner is fail-closed by design: a collector that throws is recorded as
/// an Unavailable observation and the remaining collectors still run. The
/// audit system must never crash the game because an object was unavailable,
/// a pointer was invalid, a resource was missing or runtime state changed.
/// </summary>
public static class RuntimeAuditRunner
{
    public static RuntimeAuditSession RunAll(
        string captureId,
        RuntimeAuditCollectorContext context,
        IEnumerable<IRuntimeAuditCollector> collectors,
        Action<Exception, IRuntimeAuditCollector>? onCollectorFailure = null)
    {
        var session = new RuntimeAuditSession
        {
            CaptureId = captureId,
            CapturedAtUtc = context.CapturedAtUtc,
            ActorId = context.ActorId,
            ActorName = context.ActorName,
            ObjectKind = context.ObjectKind
        };

        foreach (var collector in collectors)
        {
            RuntimeAuditCollectorResult result;
            try
            {
                result = collector.Collect(context);
            }
            catch (Exception ex)
            {
                onCollectorFailure?.Invoke(ex, collector);
                result = new RuntimeAuditCollectorResult
                {
                    Status = RuntimeAuditAvailability.Unavailable,
                    Error = $"collector failed closed: {ex.GetType().Name}: {ex.Message}",
                    Observations =
                    {
                        new RuntimeAuditObservation
                        {
                            Time = context.CapturedAtUtc.ToString("O"),
                            ActorId = context.ActorId,
                            Category = collector.Category.ToString(),
                            SourceType = "unknown",
                            Value = null,
                            Availability = RuntimeAuditAvailability.Unavailable,
                            Error = $"collector failed closed: {ex.GetType().Name}: {ex.Message}"
                        }
                    }
                };
            }

            session.CollectorStatus[collector.Category.ToString()] = result.Status;
            foreach (var observation in result.Observations)
            {
                session.AddObservation(observation with { CaptureId = captureId });
            }
        }

        return session;
    }
}
