namespace Meddle.Utils.Audit;

/// <summary>
/// Context handed to a collector. Pointers are passed as raw nint so the
/// collector contract stays free of Dalamud/FFXIVClientStructs dependencies.
/// </summary>
public sealed record RuntimeAuditCollectorContext(
    string ActorId,
    string ActorName,
    string ObjectKind,
    nint ActorAddress,
    DateTime CapturedAtUtc);

/// <summary>
/// Result of one collector run. Collectors must never throw out of
/// <see cref="IRuntimeAuditCollector.Collect"/>; failures are recorded as
/// observations with <see cref="RuntimeAuditAvailability.Unavailable"/>.
/// </summary>
public sealed class RuntimeAuditCollectorResult
{
    public RuntimeAuditAvailability Status { get; init; } = RuntimeAuditAvailability.Unknown;

    public List<RuntimeAuditObservation> Observations { get; } = [];

    public string? Error { get; init; }
}

/// <summary>
/// Contract for a runtime observation collector.
/// </summary>
public interface IRuntimeAuditCollector
{
    RuntimeAuditCategory Category { get; }

    RuntimeAuditCollectorResult Collect(RuntimeAuditCollectorContext context);
}
