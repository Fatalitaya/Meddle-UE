using System.Text.Json.Serialization;

namespace Meddle.Utils.Audit;

/// <summary>
/// One observed runtime fact.
///
/// Required fields: time, actor identity, category, source object type, value,
/// availability/state. The value payload is deliberately untyped JSON so new
/// runtime discoveries can be recorded without schema changes.
/// </summary>
public sealed record RuntimeAuditObservation
{
    /// <summary>Capture timestamp (ISO 8601 UTC).</summary>
    public required string Time { get; init; }

    /// <summary>Identity of the actor/object the observation belongs to.</summary>
    public string ActorId { get; init; } = string.Empty;

    /// <summary>Observation category, e.g. "ModelResource".</summary>
    public required string Category { get; init; }

    /// <summary>Source object type, e.g. "DrawObject", "Model", "Material".</summary>
    public required string SourceType { get; init; }

    /// <summary>Observed value. Kept in FFXIV-native representation where possible.</summary>
    public object? Value { get; init; }

    /// <summary>Confidence/state of the observation.</summary>
    public required RuntimeAuditAvailability Availability { get; init; }

    /// <summary>Human-readable reason when data was unavailable or unknown.</summary>
    public string? Error { get; init; }

    /// <summary>Capture id this observation belongs to.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CaptureId { get; init; }
}
