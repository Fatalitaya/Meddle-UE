using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Graphics.Render;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using Meddle.Plugin.Models;
using Meddle.Plugin.Models.Structs;
using Meddle.Plugin.Utils;
using Meddle.Utils.Audit;

namespace Meddle.Plugin.Audit.Collectors;

/// <summary>Reads equipment presence and visibility from the live Human and render models.</summary>
public sealed unsafe class EquipmentVisibilityCollector : IRuntimeAuditCollector
{
    public RuntimeAuditCategory Category => RuntimeAuditCategory.EquipmentVisibility;

    public RuntimeAuditCollectorResult Collect(RuntimeAuditCollectorContext context)
    {
        try
        {
            var characterBase = AuditCollectorUtil.TryGetCharacterBase(context.ActorAddress);
            if (characterBase == null || characterBase->GetModelType() != CharacterBase.ModelType.Human)
            {
                return AuditCollectorUtil.Unavailable(context, Category, "EquipmentVisibility",
                    "actor has no Human draw object");
            }

            var human = (Human*)characterBase;
            var slots = new List<object?>();
            for (var slot = 0; slot <= (int)HumanEquipmentSlotIndex.Extra; slot++)
            {
                var equipment = (&human->Head)[slot];
				// Preserve the equipment array ordinal. Equality with a model's
				// raw SlotIndex is useful correlation evidence, but it does not
				// establish Head/Body/etc. semantic identity.
				var modelSlot = (uint)slot;
                Model* model = null;
                foreach (var candidate in characterBase->ModelsSpan)
                {
                    if (candidate.Value != null && candidate.Value->SlotIndex == modelSlot)
                    {
                        model = candidate.Value;
                        break;
                    }
                }

                var equipped = equipment.Value != 0;
                bool? enabled = model == null ? null : ((MeddleModel*)model)->BodyVisible;
                bool? visible = model == null ? null : enabled == true && characterBase->DrawObject.IsVisible;
                slots.Add(new Dictionary<string, object?>
                {
                    ["slot"] = slot,
					["slotName"] = null,
					["semanticSlotStatus"] = "Unknown",
                    ["modelResource"] = model == null || model->ModelResourceHandle == null
                        ? null
                        : model->ModelResourceHandle->FileName.ParseString(),
                    ["equipped"] = equipped,
                    ["runtimeEnabled"] = enabled,
                    ["visible"] = equipped ? visible : false,
                    ["hidden"] = equipped && visible.HasValue ? !visible.Value : null,
                    ["runtimeFlags"] = model == null ? null : ((MeddleModel*)model)->Flags,
                    ["hiddenByRuntimeFlag"] = null,
					["source"] = "Human equipment raw ordinal and live render-model ordinal equality",
                    ["reason"] = equipped && model == null
                        ? "equipped runtime model has no observable render model"
                        : null
                });
            }

            return new RuntimeAuditCollectorResult
            {
                Status = RuntimeAuditAvailability.Available,
                Observations =
                {
                    AuditCollectorUtil.Observation(context, Category, "EquipmentVisibility",
                        new Dictionary<string, object?> { ["slots"] = slots })
                }
            };
        }
        catch (Exception ex)
        {
            return AuditCollectorUtil.Failed(context, Category, "EquipmentVisibility", ex);
        }
    }
}
