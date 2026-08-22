using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using Meddle.Plugin.Utils;
using Meddle.Utils.Audit;

namespace Meddle.Plugin.Audit.Collectors;

/// <summary>Captures the two live weapon draw records without resolving item IDs.</summary>
public sealed unsafe class WeaponCollector : IRuntimeAuditCollector
{
    public RuntimeAuditCategory Category => RuntimeAuditCategory.Weapon;

    public RuntimeAuditCollectorResult Collect(RuntimeAuditCollectorContext context)
    {
        try
        {
            var character = (Character*)context.ActorAddress;
            if (character == null)
                return AuditCollectorUtil.Unavailable(context, Category, "Weapon", "actor pointer is null");

            var entries = new List<object?>();
            for (var index = 0; index < character->DrawData.WeaponData.Length; index++)
            {
                var data = character->DrawData.WeaponData[index];
				var drawObject = data.DrawData.DrawObject;
				var value = new Dictionary<string, object?>
				{
					["weaponIndex"] = index,
					["slot"] = null,
					["semanticSlotStatus"] = "Unknown",
					["semanticSlotReason"] = "weapon array ordinal has no independently verified hand mapping",
                    ["visible"] = drawObject == null ? false : !data.IsHidden && drawObject->IsVisible,
                    ["hidden"] = data.IsHidden,
                    ["resource"] = null,
                    ["model"] = null,
                    ["materials"] = null,
                    ["attachment"] = null,
                    ["drawObject"] = drawObject == null ? null : $"0x{(nint)drawObject:X}"
                };
                if (drawObject != null && drawObject->GetObjectType() == ObjectType.CharacterBase)
                {
                    var weaponBase = (CharacterBase*)drawObject;
                    var models = new List<object?>();
                    var materials = new List<object?>();
                    foreach (var modelPtr in weaponBase->ModelsSpan)
                    {
                        var model = modelPtr.Value;
                        if (model == null) continue;
                        var resource = model->ModelResourceHandle == null ? null : model->ModelResourceHandle->FileName.ParseString();
                        models.Add(new Dictionary<string, object?> { ["slot"] = model->SlotIndex, ["resource"] = resource });
                        if (value["resource"] == null) value["resource"] = resource;
                        foreach (var materialPtr in model->MaterialsSpan)
                        {
                            if (materialPtr.Value == null || materialPtr.Value->MaterialResourceHandle == null) continue;
                            materials.Add(materialPtr.Value->MaterialResourceHandle->FileName.ParseString());
                        }
                    }
                    value["model"] = models;
                    value["materials"] = materials;
                }
                entries.Add(value);
            }

            return new RuntimeAuditCollectorResult
            {
                Status = RuntimeAuditAvailability.Available,
                Observations = { AuditCollectorUtil.Observation(context, Category, "Weapon", new Dictionary<string, object?> { ["weapons"] = entries }) }
            };
        }
        catch (Exception ex) { return AuditCollectorUtil.Failed(context, Category, "Weapon", ex); }
    }
}
