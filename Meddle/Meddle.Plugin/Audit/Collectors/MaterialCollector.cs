using Meddle.Plugin.Utils;
using Meddle.Utils.Audit;
using CSMaterial = FFXIVClientStructs.FFXIV.Client.Graphics.Render.Material;
using CSModel = FFXIVClientStructs.FFXIV.Client.Graphics.Render.Model;

namespace Meddle.Plugin.Audit.Collectors;

/// <summary>
/// Observes runtime material state: material identity, shader package identity,
/// texture binding count and parameter buffer availability. GPU pixel state is
/// deliberately not captured.
/// </summary>
public sealed unsafe class MaterialCollector : IRuntimeAuditCollector
{
    public RuntimeAuditCategory Category => RuntimeAuditCategory.Material;

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
            var observedAny = false;
            var materials = new List<object?>();
            foreach (var modelPtr in characterBase->ModelsSpan)
            {
                if (modelPtr == null || modelPtr.Value == null)
                {
                    continue;
                }

                var model = modelPtr.Value;
                for (var materialIndex = 0; materialIndex < model->MaterialsSpan.Length; materialIndex++)
                {
                    materials.Add(CaptureMaterial(model, materialIndex, ref observedAny));
                }
            }

            result.Observations.Add(AuditCollectorUtil.Observation(
                context,
                Category,
                "Material",
                new Dictionary<string, object?>
                {
                    ["materials"] = materials
                },
                observedAny ? RuntimeAuditAvailability.Available : result.Status));

            return result;
        }
        catch (Exception ex)
        {
            return AuditCollectorUtil.Failed(context, Category, "Material", ex);
        }
    }

    private static object? CaptureMaterial(
        CSModel* model,
        int materialIndex,
        ref bool observedAny)
    {
        var materialPtr = model->MaterialsSpan[materialIndex];
        if (materialPtr == null || materialPtr.Value == null)
        {
            return new Dictionary<string, object?>
            {
                ["materialIndex"] = materialIndex,
                ["modelSlot"] = model->SlotIndex,
                ["available"] = false
            };
        }

        var material = materialPtr.Value;
        var handle = material->MaterialResourceHandle;
        if (handle == null)
        {
            return new Dictionary<string, object?>
            {
                ["materialIndex"] = materialIndex,
                ["modelSlot"] = model->SlotIndex,
                ["available"] = false,
                ["reason"] = "Material has no MaterialResourceHandle"
            };
        }

        observedAny = true;
        var materialPath = handle->FileName.ParseString();
        string? materialPathFromModel = model->ModelResourceHandle != null
            ? model->ModelResourceHandle->GetMaterialFileNameBySlot((uint)materialIndex).ToString()
            : null;

        int? parameterFloatCount = null;
        int? parameterBufferBytes = null;
        if (material->MaterialParameterCBuffer != null)
        {
            var parameters = material->MaterialParameterCBuffer->TryGetBuffer<float>();
            parameterFloatCount = parameters.Length;
            parameterBufferBytes = parameters.Length * sizeof(float);
        }

        return new Dictionary<string, object?>
        {
            ["materialIndex"] = materialIndex,
            ["modelSlot"] = model->SlotIndex,
            ["materialAddress"] = $"0x{(nint)material:X}",
            ["path"] = materialPath,
            ["logicalPath"] = materialPathFromModel,
            ["shaderPackage"] = handle->ShpkName.ToString(),
            ["shaderFlags"] = material->ShaderFlags,
            ["refCount"] = material->RefCount,
            ["textureCount"] = material->TextureCount,
            ["hasColorTable"] = handle->HasColorTable,
            ["parameterBufferFloatCount"] = parameterFloatCount,
            ["parameterBufferBytes"] = parameterBufferBytes
        };
    }
}
