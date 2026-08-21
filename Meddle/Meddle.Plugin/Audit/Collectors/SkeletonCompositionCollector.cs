using FFXIVClientStructs.Havok.Animation.Rig;
using Meddle.Plugin.Models.Structs;
using Meddle.Plugin.Utils;
using Meddle.Utils.Audit;
using Skeleton = FFXIVClientStructs.FFXIV.Client.Graphics.Render.Skeleton;

namespace Meddle.Plugin.Audit.Collectors;

/// <summary>
/// Observes the runtime skeleton composition. This is evidence only: handles,
/// bone counts, hierarchy availability and parent relationships as exposed by
/// the runtime. No SKLB parsing, no skeleton generation.
/// </summary>
public sealed unsafe class SkeletonCompositionCollector : IRuntimeAuditCollector
{
    public RuntimeAuditCategory Category => RuntimeAuditCategory.SkeletonEvidence;

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

            var skeleton = characterBase->Skeleton;
            if (skeleton == null)
            {
                return AuditCollectorUtil.Unavailable(
                    context,
                    Category,
                    "Skeleton",
                    "CharacterBase has no Skeleton");
            }

            if (skeleton->PartialSkeletonCount == 0)
            {
                return AuditCollectorUtil.Unknown(
                    context,
                    Category,
                    "Skeleton",
                    "Skeleton exists but exposes no partial skeletons");
            }

            var result = new RuntimeAuditCollectorResult { Status = RuntimeAuditAvailability.Available };
            var partials = new List<object?>();
            for (var i = 0; i < skeleton->PartialSkeletonCount; i++)
            {
                partials.Add(CapturePartialSkeleton(&skeleton->PartialSkeletons[i], i));
            }

            result.Observations.Add(AuditCollectorUtil.Observation(
                context,
                Category,
                "Skeleton",
                new Dictionary<string, object?>
                {
                    ["partialSkeletonCount"] = skeleton->PartialSkeletonCount,
                    ["skeletonAddress"] = $"0x{(nint)skeleton:X}",
                    ["partialSkeletons"] = partials,
                    ["attachRelationships"] = CaptureAttachRelationships(characterBase)
                }));

            return result;
        }
        catch (Exception ex)
        {
            return AuditCollectorUtil.Failed(context, Category, "Skeleton", ex);
        }
    }

    private static object? CapturePartialSkeleton(
        FFXIVClientStructs.FFXIV.Client.Graphics.Render.PartialSkeleton* partial,
        int index)
    {
        if (partial == null)
        {
            return new Dictionary<string, object?>
            {
                ["partialSkeletonIndex"] = index,
                ["available"] = false
            };
        }

        var extended = (PartialSkeletonEx*)partial;
        var handle = partial->SkeletonResourceHandle;
        var handlePath = handle == null ? null : handle->FileName.ParseString();
        var havok = handle == null ? null : handle->HavokSkeleton;

        var value = new Dictionary<string, object?>
        {
            ["partialSkeletonIndex"] = index,
            ["handle"] = handlePath,
            ["resourceIdentityAvailable"] = handle != null,
            ["boneCount"] = extended->BoneCount,
            ["connectedBoneIndex"] = partial->ConnectedBoneIndex,
            ["partialSkeletonFlags"] = extended->PartialSkeletonFlags,
            ["poseCount"] = partial->HavokPoses.Length,
            ["hierarchyAvailable"] = havok != null,
            ["parentRelationship"] = havok != null ? "available" : "unknown"
        };

        if (havok != null)
        {
            var boneCount = havok->Bones.Length;
            var parentIndices = new List<short>(boneCount);
            var boneNames = new List<string?>(boneCount);
            for (var boneIndex = 0; boneIndex < boneCount; boneIndex++)
            {
                parentIndices.Add(havok->ParentIndices[boneIndex]);
                boneNames.Add(havok->Bones[boneIndex].Name.String);
            }

            value["boneNames"] = boneNames;
            value["parentIndices"] = parentIndices;
            value["boneNameCount"] = boneNames.Count(n => !string.IsNullOrEmpty(n));
        }

        return value;
    }

    private static object? CaptureAttachRelationships(
        FFXIVClientStructs.FFXIV.Client.Graphics.Scene.CharacterBase* characterBase)
    {
        var attach = characterBase->Attach;
        if (attach.AttachmentCount <= 0)
        {
            return new Dictionary<string, object?>
            {
                ["attachmentCount"] = 0
            };
        }

        return new Dictionary<string, object?>
        {
            ["executeType"] = attach.ExecuteType,
            ["attachmentCount"] = attach.AttachmentCount,
            ["ownerCharacterSkeleton"] = attach.OwnerCharacter != null
                ? DescribeSkeleton(attach.OwnerCharacter->Skeleton)
                : null,
            ["ownerSkeleton"] = attach.OwnerSkeleton != null
                ? DescribeSkeleton(attach.OwnerSkeleton)
                : null,
            ["targetSkeleton"] = attach.TargetSkeleton != null
                ? DescribeSkeleton(attach.TargetSkeleton)
                : null
        };
    }

    private static Dictionary<string, object?>? DescribeSkeleton(Skeleton* skeleton)
    {
        if (skeleton == null)
        {
            return null;
        }

        var partial = skeleton->PartialSkeletonCount > 0
            ? &skeleton->PartialSkeletons[0]
            : null;
        var handle = partial != null ? partial->SkeletonResourceHandle : null;
        return new Dictionary<string, object?>
        {
            ["handle"] = handle == null ? null : handle->FileName.ParseString(),
            ["partialSkeletonCount"] = skeleton->PartialSkeletonCount
        };
    }
}
