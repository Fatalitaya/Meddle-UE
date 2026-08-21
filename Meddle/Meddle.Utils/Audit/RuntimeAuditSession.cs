using System.Text.Json.Serialization;

namespace Meddle.Utils.Audit;

/// <summary>
/// A completed (or in-progress) audit capture: one actor, one moment in time
/// (or a short observation window), and the observations collected from it.
/// </summary>
public sealed class RuntimeAuditSession
{
    /// <summary>Every audit file is marked as experimental; this is not a snapshot ABI.</summary>
    public string Format => RuntimeAuditFormat.FormatMarker;

    public int SchemaVersion => RuntimeAuditFormat.SchemaVersion;

    public required string CaptureId { get; init; }

    public required DateTime CapturedAtUtc { get; init; }

    /// <summary>Runtime actor identity (object address or object id as hex).</summary>
    public string ActorId { get; init; } = string.Empty;

    public string ActorName { get; init; } = string.Empty;

    public string ObjectKind { get; init; } = string.Empty;

    /// <summary>Generator identity, e.g. "Meddle audit v1".</summary>
    public string Generator { get; init; } = "Meddle runtime audit";

    public List<RuntimeAuditObservation> Observations { get; init; } = [];

    /// <summary>Per-category collector state summary.</summary>
    public Dictionary<string, RuntimeAuditAvailability> CollectorStatus { get; init; } = new();

    [JsonIgnore]
    public bool IsEmpty => Observations.Count == 0;

    public void AddObservation(RuntimeAuditObservation observation)
    {
        Observations.Add(observation);
    }

    public IReadOnlyList<RuntimeAuditObservation> ForCategory(RuntimeAuditCategory category)
    {
        return Observations
               .Where(o => string.Equals(o.Category, category.ToString(), StringComparison.Ordinal))
               .ToArray();
    }
}
