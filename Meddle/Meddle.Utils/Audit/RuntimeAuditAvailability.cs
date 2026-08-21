namespace Meddle.Utils.Audit;

/// <summary>
/// State of a collected observation.
///
/// Every collector must explicitly report one of these states. "Unknown" means
/// the audit framework cannot tell whether the data exists (for example the
/// runtime object was valid but no data could be read without risk).
/// </summary>
public enum RuntimeAuditAvailability
{
    /// <summary>The runtime exposed the data and it was captured.</summary>
    Available,

    /// <summary>The runtime did not expose the data (or it is not applicable to this target).</summary>
    Unavailable,

    /// <summary>The audit framework could not determine whether the data exists.</summary>
    Unknown
}
