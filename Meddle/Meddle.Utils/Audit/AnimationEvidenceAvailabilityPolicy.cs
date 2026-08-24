namespace Meddle.Utils.Audit;

/// <summary>
/// Availability policy for the AnimationEvidence audit category.
///
/// Partial evidence is still evidence:
/// <list type="bullet">
/// <item>no CharacterBase -&gt; <see cref="RuntimeAuditAvailability.Unavailable"/></item>
/// <item>CharacterBase + both PAP hooks -&gt; <see cref="RuntimeAuditAvailability.Available"/></item>
/// <item>CharacterBase + only one or zero PAP hooks -&gt;
/// <see cref="RuntimeAuditAvailability.Unknown"/> (skeleton handle identity may
/// still be observed; the specific hook capability is reported separately).</item>
/// </list>
/// A zero-length recent-PAP history does NOT downgrade availability: it means
/// no PAP loads were observed in the current bounded window, not that PAP
/// evidence is unavailable.
/// </summary>
public static class AnimationEvidenceAvailabilityPolicy
{
    public static RuntimeAuditAvailability Evaluate(
        bool characterBaseAvailable,
        bool syncHookAvailable,
        bool asyncHookAvailable)
    {
        if (!characterBaseAvailable)
        {
            return RuntimeAuditAvailability.Unavailable;
        }

        return syncHookAvailable && asyncHookAvailable
            ? RuntimeAuditAvailability.Available
            : RuntimeAuditAvailability.Unknown;
    }
}
