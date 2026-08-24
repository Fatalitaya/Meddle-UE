using Meddle.Plugin.Utils;
using Meddle.Utils.Audit;
using Meddle.Formats.Files;
using CSMaterial = FFXIVClientStructs.FFXIV.Client.Graphics.Render.Material;
using CSModel = FFXIVClientStructs.FFXIV.Client.Graphics.Render.Model;

namespace Meddle.Plugin.Audit.Collectors;

/// <summary>
/// Identifies runtime texture bindings: handles, source paths, resource ids,
/// generated/runtime status and dimensions. Textures are never decoded here.
/// </summary>
public sealed unsafe class RuntimeTextureCollector : IRuntimeAuditCollector
{
    public RuntimeAuditCategory Category => RuntimeAuditCategory.RuntimeTexture;

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
            var bindings = new List<object?>();
            foreach (var materialRoot in AuditCollectorUtil.EnumerateMaterialRoots(context.ActorAddress))
            {
                var currentBase = (FFXIVClientStructs.FFXIV.Client.Graphics.Scene.CharacterBase*)materialRoot.Address;
                foreach (var modelPtr in currentBase->ModelsSpan)
                {
                    if (modelPtr == null || modelPtr.Value == null)
                    {
                        continue;
                    }

                    var model = modelPtr.Value;
                    for (var materialIndex = 0; materialIndex < model->MaterialsSpan.Length; materialIndex++)
                    {
                        var materialPtr = model->MaterialsSpan[materialIndex];
                        if (materialPtr == null || materialPtr.Value == null)
                        {
                            continue;
                        }

                        var handle = materialPtr.Value->MaterialResourceHandle;
                        if (handle == null)
                        {
                            continue;
                        }

                        for (var textureIndex = 0; textureIndex < handle->TexturesSpan.Length; textureIndex++)
                        {
                            bindings.Add(CaptureBinding(
                                materialPtr.Value,
                                handle,
                                model->SlotIndex,
                                materialIndex,
                                textureIndex,
                                materialRoot.OwnerKind,
                                materialRoot.WeaponIndex,
                                ref observedAny));
                        }
                    }
                }
            }

            result.Observations.Add(AuditCollectorUtil.Observation(
                context,
                Category,
                "TextureResourceHandle",
                new Dictionary<string, object?>
                {
                    ["bindings"] = bindings
                },
                observedAny ? RuntimeAuditAvailability.Available : result.Status));

            return result;
        }
        catch (Exception ex)
        {
            return AuditCollectorUtil.Failed(context, Category, "TextureResourceHandle", ex);
        }
    }

    private static object? CaptureBinding(
        CSMaterial* material,
        FFXIVClientStructs.FFXIV.Client.System.Resource.Handle.MaterialResourceHandle* materialHandle,
        uint modelSlot,
        int materialIndex,
        int textureIndex,
        string ownerKind,
        int? weaponIndex,
        ref bool observedAny)
    {
        var binding = materialHandle->TexturesSpan[textureIndex];
        var textureResourceHandle = binding.TextureResourceHandle;
        if (textureResourceHandle == null)
        {
            return new Dictionary<string, object?>
            {
                ["modelSlot"] = modelSlot,
                ["materialIndex"] = materialIndex,
                ["index"] = textureIndex,
                ["ownerKind"] = ownerKind,
                ["weaponIndex"] = weaponIndex,
                ["available"] = false
            };
        }

        var texture = textureResourceHandle->Texture;
        if (texture == null)
        {
            return new Dictionary<string, object?>
            {
                ["modelSlot"] = modelSlot,
                ["materialIndex"] = materialIndex,
                ["index"] = textureIndex,
                ["ownerKind"] = ownerKind,
                ["weaponIndex"] = weaponIndex,
                ["available"] = false,
                ["reason"] = "TextureResourceHandle has no Texture"
            };
        }

        observedAny = true;
        var sourcePath = textureIndex < material->TextureCount
            ? materialHandle->TexturePath(textureIndex).ToString()
            : null;
        var resourcePath = textureResourceHandle->FileName.ParseString();
        var generatedEvidence = RuntimeTextureEvidence.ClassifyGeneratedStatus(
            sourcePath,
            resourcePath,
            hasExplicitRuntimeGeneratedEvidence: false);

        uint? bindingId = null;
        uint? samplerFlags = null;
        if (textureIndex < material->TexturesSpan.Length)
        {
            var materialBinding = material->TexturesSpan[textureIndex];
            bindingId = materialBinding.Id;
            samplerFlags = materialBinding.SamplerFlags;
        }

        return new Dictionary<string, object?>
        {
            ["modelSlot"] = modelSlot,
            ["materialIndex"] = materialIndex,
            ["index"] = textureIndex,
            ["ownerKind"] = ownerKind,
            ["weaponIndex"] = weaponIndex,
            ["bindingId"] = bindingId,
            ["samplerFlags"] = samplerFlags,
            ["runtimeHandle"] = $"0x{(nint)texture:X}",
            ["sourcePath"] = sourcePath,
            ["resourcePath"] = resourcePath,
            ["fileSize"] = textureResourceHandle->FileSize,
            ["generatedStatus"] = generatedEvidence.Status,
            ["generatedReason"] = generatedEvidence.Reason,
            ["width"] = texture->ActualWidth,
            ["height"] = texture->ActualHeight,
            ["depth"] = texture->Depth,
            ["mipLevels"] = texture->MipLevel,
            ["arraySize"] = texture->ArraySize,
            ["format"] = ((TexFile.TextureFormat)texture->TextureFormat).ToString()
        };
    }

}
