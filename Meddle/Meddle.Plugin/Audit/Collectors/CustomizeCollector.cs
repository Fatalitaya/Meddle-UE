using System.Numerics;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using Meddle.Plugin.Models;
using Meddle.Utils.Audit;
using CustomizeParameterStruct = Meddle.Plugin.Models.Structs.CustomizeParameter;

namespace Meddle.Plugin.Audit.Collectors;

/// <summary>
/// Captures currently resolved character appearance data in FFXIV-native
/// representation. Values are raw runtime bytes/numbers; nothing is converted.
/// </summary>
public sealed unsafe class CustomizeCollector : IRuntimeAuditCollector
{
    public RuntimeAuditCategory Category => RuntimeAuditCategory.Customize;

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

            if (characterBase->GetModelType() != CharacterBase.ModelType.Human)
            {
                return AuditCollectorUtil.Unavailable(
                    context,
                    Category,
                    "Human",
                    $"customize data not applicable to {characterBase->GetModelType()}");
            }

            var human = (Human*)characterBase;
            var customize = human->Customize;
            var result = new RuntimeAuditCollectorResult { Status = RuntimeAuditAvailability.Available };
            result.Observations.Add(AuditCollectorUtil.Observation(
                context,
                Category,
                "Human",
                new Dictionary<string, object?>
                {
                    ["raceSexId"] = human->RaceSexId,
                    ["customize"] = new Dictionary<string, object?>
                    {
                        ["race"] = customize.Race,
                        ["sex"] = customize.Sex,
                        ["bodyType"] = customize.BodyType,
                        ["height"] = customize.Height,
                        ["tribe"] = customize.Tribe,
                        ["face"] = customize.Face,
                        ["hairstyle"] = customize.Hairstyle,
                        ["highlights"] = customize.Highlights,
                        ["skinColor"] = customize.SkinColor,
                        ["eyeColorLeft"] = customize.EyeColorLeft,
                        ["eyeColorRight"] = customize.EyeColorRight,
                        ["hairColor"] = customize.HairColor,
                        ["facePaint"] = customize.FacePaint,
                        ["facePaintColor"] = customize.FacePaintColor,
                        ["facePaintReversed"] = customize.FacePaintReversed,
                        ["lipColorFurPattern"] = customize.LipColorFurPattern,
                        ["lipstick"] = customize.Lipstick,
                        ["muscleMass"] = customize.MuscleMass,
                        ["tailShape"] = customize.TailShape,
                        ["bustSize"] = customize.BustSize,
                        ["eyebrows"] = customize.Eyebrows,
                        ["nose"] = customize.Nose,
                        ["jaw"] = customize.Jaw,
                        ["tattooColor"] = customize.TattooColor,
                        ["eyeShape"] = customize.EyeShape,
                        ["smallIris"] = customize.SmallIris,
                        ["mouth"] = customize.Mouth,
                        ["legacyTattoo"] = customize.LegacyTattoo
                    },
                    ["parameters"] = CaptureParameters(human),
                    ["decalColor"] = CaptureDecalColor(human),
                    ["equipmentModelIds"] = CaptureEquipment(human),
                    ["faceDecal"] = CaptureFaceDecal(human)
                }));

            return result;
        }
        catch (Exception ex)
        {
            return AuditCollectorUtil.Failed(context, Category, "Human", ex);
        }
    }

    private static object? CaptureParameters(Human* human)
    {
        if (human->CustomizeParameterCBuffer == null)
        {
            return new Dictionary<string, object?> { ["available"] = false };
        }

        var buffer = human->CustomizeParameterCBuffer->TryGetBuffer<CustomizeParameterStruct>();
        if (buffer.Length == 0)
        {
            return new Dictionary<string, object?> { ["available"] = false };
        }

        var parameters = buffer[0];
        return new Dictionary<string, object?>
        {
            ["available"] = true,
            ["skinColor"] = AsArray(parameters.SkinColor),
            ["muscleTone"] = parameters.MuscleTone,
            ["lipColor"] = AsArray(parameters.LipColor),
            ["mainColor"] = AsArray(parameters.MainColor),
            ["facePaintUvMultiplier"] = parameters.FacePaintUVMultiplier,
            ["meshColor"] = AsArray(parameters.MeshColor),
            ["facePaintUvOffset"] = parameters.FacePaintUVOffset,
            ["leftColor"] = AsArray(parameters.LeftColor),
            ["rightColor"] = AsArray(parameters.RightColor),
            ["optionColor"] = AsArray(parameters.OptionColor)
        };
    }

    private static object? CaptureDecalColor(Human* human)
    {
        if (human->DecalColorCBuffer == null)
        {
            return new Dictionary<string, object?> { ["available"] = false };
        }

        var buffer = human->DecalColorCBuffer->TryGetBuffer<Vector4>();
        if (buffer.Length == 0)
        {
            return new Dictionary<string, object?> { ["available"] = false };
        }

        return new Dictionary<string, object?>
        {
            ["available"] = true,
            ["color"] = AsArray(buffer[0])
        };
    }

    private static List<object?> CaptureEquipment(Human* human)
    {
        var equipment = new List<object?>();
        for (var slot = 0; slot <= (int)HumanEquipmentSlotIndex.Extra; slot++)
        {
            var modelId = (&human->Head)[slot];
			equipment.Add(new Dictionary<string, object?>
			{
				["slot"] = slot,
				["slotName"] = null,
				["semanticSlotStatus"] = "Unknown",
                ["id"] = modelId.Id,
                ["variant"] = modelId.Variant,
                ["stain0"] = modelId.Stain0,
                ["stain1"] = modelId.Stain1,
                ["value"] = modelId.Value
            });
        }

        return equipment;
    }

    private static object? CaptureFaceDecal(Human* human)
    {
        var decal = human->Decal;
        if (decal == null)
        {
            return new Dictionary<string, object?> { ["present"] = false };
        }

        return new Dictionary<string, object?>
        {
            ["present"] = true,
            ["width"] = decal->Texture != null ? decal->Texture->ActualWidth : 0,
            ["height"] = decal->Texture != null ? decal->Texture->ActualHeight : 0,
            ["loadState"] = decal->LoadState
        };
    }

    private static float[] AsArray(Vector3 value) => [value.X, value.Y, value.Z];

    private static float[] AsArray(Vector4 value) => [value.X, value.Y, value.Z, value.W];
}
