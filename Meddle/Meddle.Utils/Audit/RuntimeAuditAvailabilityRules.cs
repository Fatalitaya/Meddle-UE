namespace Meddle.Utils.Audit;

/// <summary>
/// Combines collector summaries across a continuous capture. Individual
/// observations remain the authoritative timeline; the summary records the
/// strongest evidence seen so that an early Unknown cannot hide a later
/// successful observation.
/// </summary>
public static class RuntimeAuditAvailabilityRules
{
    public static RuntimeAuditAvailability Merge(
        RuntimeAuditAvailability current,
        RuntimeAuditAvailability incoming)
    {
        if (current == RuntimeAuditAvailability.Available ||
            incoming == RuntimeAuditAvailability.Available)
        {
            return RuntimeAuditAvailability.Available;
        }

        if (current == RuntimeAuditAvailability.Unavailable ||
            incoming == RuntimeAuditAvailability.Unavailable)
        {
            return RuntimeAuditAvailability.Unavailable;
        }

        return RuntimeAuditAvailability.Unknown;
    }
}
