using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using Meddle.Plugin.Models;
using Meddle.Plugin.Models.Structs;
using Meddle.Plugin.Utils;
using Meddle.Utils.Audit;

namespace Meddle.Plugin.Audit.Collectors;

/// <summary>
/// Observes the actual runtime model graph: CharacterBase, DrawObject and
/// Model resources. Reports slot indexes and raw resource identities only;
/// semantic slots (Head/Body/Hair/Weapon) are never invented here.
/// </summary>
public sealed unsafe class CharacterCompositionCollector : IRuntimeAuditCollector
{
    public RuntimeAuditCategory Category => RuntimeAuditCategory.CharacterComposition;

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

            var result = new RuntimeAuditCollectorResult { Status = RuntimeAuditAvailability.Available };
            var modelType = characterBase->GetModelType();
            var character = (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)context.ActorAddress;
            if (character != null)
            {
                result.Observations.Add(AuditCollectorUtil.Observation(
                    context,
                    Category,
                    "GameObject",
                    new Dictionary<string, object?>
                    {
                        ["objectId"] = character->EntityId,
                        ["dataId"] = character->LayoutId,
                        ["modelId"] = character->ModelContainer.ModelCharaId,
                        ["objectKind"] = context.ObjectKind,
                        ["actorAddress"] = $"0x{context.ActorAddress:X}"
                    }));
            }

            result.Observations.Add(AuditCollectorUtil.Observation(
                context,
                Category,
                "CharacterBase",
                new Dictionary<string, object?>
                {
                    ["modelType"] = modelType.ToString(),
                    ["objectKind"] = context.ObjectKind,
                    ["drawObjectVisible"] = ((DrawObject*)characterBase)->IsVisible,
                    ["actorAddress"] = $"0x{context.ActorAddress:X}"
                }));

            var models = new List<object?>();
            var modelResources = new List<object?>();
            foreach (var modelPtr in characterBase->ModelsSpan)
            {
                if (modelPtr == null || modelPtr.Value == null)
                {
                    continue;
                }

                var model = modelPtr.Value;
                var slotIndex = model->SlotIndex;
                if (model->ModelResourceHandle == null)
                {
                    result.Observations.Add(AuditCollectorUtil.Observation(
                        context,
                        Category,
                        "Model",
                        new Dictionary<string, object?> { ["slotIndex"] = slotIndex },
                        RuntimeAuditAvailability.Unknown,
                        "Model has no ModelResourceHandle"));
                    continue;
                }

                modelResources.Add(CaptureModelResource(context, model, slotIndex));
                var resourcePath = model->ModelResourceHandle->FileName.ParseString();
                string logicalPath = characterBase->ResolveMdlPath(slotIndex);
                models.Add(new Dictionary<string, object?>
                {
                    ["slotIndex"] = slotIndex,
                    ["resource"] = resourcePath,
                    ["logicalPath"] = logicalPath,
                    ["enabled"] = ((MeddleModel*)model)->BodyVisible,
					// Model.SlotIndex and the Human equipment array are distinct
					// runtime identities. Do not cast one ordinal into the other.
					["variant"] = null,
					["variantReason"] = "no independently verified model-slot to equipment-slot mapping",
                    ["relation"] = "unknown",
                    ["modelAddress"] = $"0x{(nint)model:X}"
                });
            }

            if (modelResources.Count > 0)
            {
                result.Observations.Add(AuditCollectorUtil.Observation(
                    context,
                    Category,
                    "ModelResource",
                    new Dictionary<string, object?>
                    {
                        ["models"] = modelResources
                    }));
            }

            result.Observations.Add(AuditCollectorUtil.Observation(
                context,
                Category,
                "DrawObject",
                new Dictionary<string, object?>
                {
                    ["models"] = models,
                    ["attach"] = CaptureAttach(characterBase)
                }));

            return result;
        }
        catch (Exception ex)
        {
            return AuditCollectorUtil.Failed(context, Category, "CharacterBase", ex);
        }
    }

    private static Dictionary<string, object?> CaptureModelResource(
        RuntimeAuditCollectorContext context,
        FFXIVClientStructs.FFXIV.Client.Graphics.Render.Model* model,
        uint slotIndex)
    {
        var value = new Dictionary<string, object?>
        {
            ["slotIndex"] = slotIndex,
            ["resource"] = model->ModelResourceHandle->FileName.ParseString(),
            ["meshCount"] = null,
            ["submeshCount"] = null,
            ["vertexDeclarationCount"] = null,
            ["materialCount"] = null,
            ["boneCount"] = null,
            ["shapeCount"] = null,
            ["headerAvailable"] = false
        };

        try
        {
            var handle = (MeddleModelResourceHandle*)model->ModelResourceHandle;
            if (handle == null || handle->Base == null || handle->Base->ModelData == null)
            {
                value["reason"] = "model resource handle has no loaded model data";
                return value;
            }

            var header = handle->GetData().ModelHeader;
            value["meshCount"] = header.MeshCount;
            value["submeshCount"] = header.SubmeshCount;
            value["vertexDeclarationCount"] = header.ElementIdCount;
            value["materialCount"] = header.MaterialCount;
            value["boneCount"] = header.BoneCount;
            value["shapeCount"] = header.ShapeCount;
            value["headerAvailable"] = true;
        }
        catch (Exception ex)
        {
            value["reason"] = $"model header could not be read safely: {ex.GetType().Name}: {ex.Message}";
        }

        return value;
    }

    private static Dictionary<string, object?>? CaptureAttach(CharacterBase* characterBase)
    {
        var attach = characterBase->Attach;
        if (attach.AttachmentCount <= 0 && attach.ExecuteType == 0)
        {
            return new Dictionary<string, object?>
            {
                ["executeType"] = attach.ExecuteType,
                ["attachmentCount"] = 0
            };
        }

        var value = new Dictionary<string, object?>
        {
            ["executeType"] = attach.ExecuteType,
            ["attachmentCount"] = attach.AttachmentCount
        };
        if (attach.AttachmentCount > 0)
        {
            var boneAttachment = attach.SkeletonBoneAttachments[0];
            value["partialSkeletonIdx"] = boneAttachment.BoneIndexMask.PartialSkeletonIdx;
            value["boneIdx"] = boneAttachment.BoneIndexMask.BoneIdx;
        }

        return value;
    }
}
