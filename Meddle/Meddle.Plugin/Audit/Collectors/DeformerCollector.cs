using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using Meddle.Plugin.Services;
using Meddle.Utils.Audit;
using Meddle.Utils.Export.Ue;

namespace Meddle.Plugin.Audit.Collectors;

/// <summary>
/// Records whether the runtime exposes deformation information. Relies on the
/// existing PbdHooks observation cache (Human_CreateDeformer hook). No
/// reconstruction is attempted; only exists / unavailable / unknown is
/// recorded per model slot.
/// </summary>
public sealed unsafe class DeformerCollector : IRuntimeAuditCollector
{
    private readonly PbdHooks pbdHooks;

    public DeformerCollector(PbdHooks pbdHooks)
    {
        this.pbdHooks = pbdHooks;
    }

    public RuntimeAuditCategory Category => RuntimeAuditCategory.Deformer;

    public RuntimeAuditCollectorResult Collect(RuntimeAuditCollectorContext context)
    {
        try
        {
            var characterBase = AuditCollectorUtil.TryGetCharacterBase(context.ActorAddress);
            if (characterBase == null)
            {
                return AuditCollectorUtil.Unavailable(
                    context,
                    Category,
                    "CharacterBase",
                    "actor has no CharacterBase draw object");
            }

            var result = new RuntimeAuditCollectorResult { Status = RuntimeAuditAvailability.Unknown };
            var slots = new List<object?>();
            foreach (var modelPtr in characterBase->ModelsSpan)
            {
                if (modelPtr == null || modelPtr.Value == null)
                {
                    continue;
                }

                var slot = modelPtr.Value->SlotIndex;
                var observation = pbdHooks.TryGetObservation((nint)characterBase, slot);
                slots.Add(CaptureSlot(slot, observation));
            }

            var cacheSnapshots = pbdHooks.GetDeformerCache();
            result.Observations.Add(AuditCollectorUtil.Observation(
                context,
                Category,
                "Deformer",
                new Dictionary<string, object?>
                {
                    ["slots"] = slots,
                    ["hookCacheEntryCount"] = cacheSnapshots.Count
                },
                slots.Any(s => IsAvailable(s)) ? RuntimeAuditAvailability.Available : result.Status));

            return result;
        }
        catch (Exception ex)
        {
            return AuditCollectorUtil.Failed(context, Category, "Deformer", ex);
        }
    }

    private static bool IsAvailable(object? slot)
    {
        return slot is Dictionary<string, object?> dict &&
               dict.TryGetValue("state", out var state) &&
               string.Equals(state?.ToString(), "exists", StringComparison.Ordinal);
    }

    private static Dictionary<string, object?> CaptureSlot(uint slot, DeformerObservation? observation)
    {
        if (observation == null)
        {
            return new Dictionary<string, object?>
            {
                ["slotIndex"] = slot,
                ["state"] = "unknown",
                ["reason"] = "no deformer observation cached for this slot"
            };
        }

        var value = new Dictionary<string, object?>
        {
            ["slotIndex"] = slot
        };
        switch (observation.State)
        {
            case DeformerObservationState.Observed:
                value["state"] = "exists";
                value["deformerId"] = observation.DeformerId;
                value["raceSexId"] = observation.RaceSexId;
                value["pbdPath"] = observation.PbdPath;
                break;
            case DeformerObservationState.ObservedNone:
                value["state"] = "none";
                value["reason"] = "runtime observed that no deformer exists";
                break;
            case DeformerObservationState.ObservedIncomplete:
                value["state"] = "incomplete";
                value["deformerId"] = observation.DeformerId;
                value["raceSexId"] = observation.RaceSexId;
                value["reason"] = "runtime hook returned incomplete information";
                break;
            case DeformerObservationState.NotApplicable:
                value["state"] = "unavailable";
                value["reason"] = "object type does not support deformers";
                break;
            default:
                value["state"] = "unknown";
                value["reason"] = "deformer observation state not classified";
                break;
        }

        return value;
    }
}
