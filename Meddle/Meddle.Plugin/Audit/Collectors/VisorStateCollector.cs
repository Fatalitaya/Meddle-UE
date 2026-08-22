using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using Meddle.Utils.Audit;

namespace Meddle.Plugin.Audit.Collectors;

/// <summary>Captures the visor runtime flag independently of helmet visibility.</summary>
public sealed unsafe class VisorStateCollector : IRuntimeAuditCollector
{
    public RuntimeAuditCategory Category => RuntimeAuditCategory.VisorState;

    public RuntimeAuditCollectorResult Collect(RuntimeAuditCollectorContext context)
    {
        try
        {
            var characterBase = AuditCollectorUtil.TryGetCharacterBase(context.ActorAddress);
            if (characterBase == null)
                return AuditCollectorUtil.Unavailable(context, Category, "VisorState", "actor has no CharacterBase draw object");

            return new RuntimeAuditCollectorResult
            {
                Status = RuntimeAuditAvailability.Available,
                Observations =
                {
                    AuditCollectorUtil.Observation(context, Category, "VisorState", new Dictionary<string, object?>
                    {
                        // The runtime exposes the toggle but not whether the equipped head model supports it.
                        ["supported"] = null,
                        ["active"] = characterBase->VisorToggled,
                        ["state"] = characterBase->VisorToggled ? "active" : "inactive",
                        ["isChanging"] = characterBase->IsChangingVisor,
                        ["affectedObjects"] = Array.Empty<object?>()
                    })
                }
            };
        }
        catch (Exception ex) { return AuditCollectorUtil.Failed(context, Category, "VisorState", ex); }
    }
}
