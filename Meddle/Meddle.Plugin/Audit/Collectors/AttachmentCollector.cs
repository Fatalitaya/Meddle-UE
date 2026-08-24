using System.Numerics;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Graphics.Render;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using Meddle.Plugin.Models;
using Meddle.Plugin.Utils;
using Meddle.Utils.Audit;

namespace Meddle.Plugin.Audit.Collectors;

/// <summary>Captures observable weapon attachment endpoints and local transforms.</summary>
public sealed unsafe class AttachmentCollector : IRuntimeAuditCollector
{
    private readonly SigUtil sigUtil;

    public AttachmentCollector(SigUtil sigUtil)
    {
        this.sigUtil = sigUtil;
    }

    public RuntimeAuditCategory Category => RuntimeAuditCategory.Attachment;

    public RuntimeAuditCollectorResult Collect(RuntimeAuditCollectorContext context)
    {
        try
        {
            var character = (Character*)context.ActorAddress;
            var parent = AuditCollectorUtil.TryGetCharacterBase(context.ActorAddress);
            if (character == null || parent == null)
                return AuditCollectorUtil.Unavailable(context, Category, "Attachment", "actor has no CharacterBase draw object");

            var attachments = new List<object?>();
            foreach (var weaponData in character->DrawData.WeaponData)
            {
                var drawObject = weaponData.DrawData.DrawObject;
                if (drawObject == null || drawObject->GetObjectType() != ObjectType.CharacterBase)
                    continue;

                var child = (CharacterBase*)drawObject;
                attachments.Add(Capture(parent, child));
            }

            var linked = LinkedAttachEvidence.CaptureEvidence(character, parent, sigUtil);
            var linkedAvailability = linked.Traversal.Status switch
            {
                LinkedAttachTraversalStatus.Completed => RuntimeAuditAvailability.Available,
                LinkedAttachTraversalStatus.Unavailable => RuntimeAuditAvailability.Unavailable,
                _ => RuntimeAuditAvailability.Unknown
            };

            var result = new RuntimeAuditCollectorResult
            {
                // The explicit weapon/attachment observation is still valid even
                // when the optional linked traversal is unavailable or incomplete.
                // An incomplete linked view downgrades the overall category to
                // Unknown, but never erases the explicit Available evidence.
                Status = LinkedAttachAvailability.Combine(
                    explicitAvailable: true,
                    linkedAvailability),
                Observations =
                {
                    AuditCollectorUtil.Observation(
                        context,
                        Category,
                        "Attachment",
                        new Dictionary<string, object?> { ["attachments"] = attachments },
                        RuntimeAuditAvailability.Available)
                }
            };

            result.Observations.Add(AuditCollectorUtil.Observation(
                context,
                Category,
                "LinkedAttach",
                BuildLinkedValue(linked),
                linkedAvailability,
                linked.Traversal.Reason));

            return result;
        }
        catch (Exception ex) { return AuditCollectorUtil.Failed(context, Category, "Attachment", ex); }
    }

    private static Dictionary<string, object?> BuildLinkedValue(LinkedAttachEvidence.Capture linked)
    {
        var nodes = new List<object?>();
        foreach (var node in linked.Traversal.Nodes)
        {
            var (interpretation, status) = LinkedAttachSemantics.InterpretExecuteType(node.ExecuteType);
            nodes.Add(new Dictionary<string, object?>
            {
                ["address"] = $"0x{node.Address:X}",
                ["vtable"] = $"0x{node.VTable:X}",
                ["previous"] = node.Previous == 0 ? null : $"0x{node.Previous:X}",
                ["next"] = node.Next == 0 ? null : $"0x{node.Next:X}",
                ["executeTypeRaw"] = node.ExecuteType,
                ["executeTypeInterpretation"] = interpretation,
                ["interpretationStatus"] = status,
                ["targetSkeleton"] = node.TargetSkeleton == 0 ? null : $"0x{node.TargetSkeleton:X}",
                ["ownerUnion"] = node.OwnerUnion == 0 ? null : $"0x{node.OwnerUnion:X}",
                ["attachmentCount"] = node.AttachmentCount,
                ["attachmentsPointer"] = node.AttachmentsPointer == 0 ? null : $"0x{node.AttachmentsPointer:X}"
            });
        }

        var resolved = linked.ResolvedCharacterBases.Select(r => (object?)new Dictionary<string, object?>
        {
            ["address"] = $"0x{r.Address:X}",
            ["classification"] = r.Classification,
            ["executeType"] = r.ExecuteType
        }).ToList();

        var extra = linked.ResolvedCharacterBases
                          .Where(r => string.Equals(r.Classification, "extra", StringComparison.Ordinal))
                          .Select(r => $"0x{r.Address:X}")
                          .ToList();

        return new Dictionary<string, object?>
        {
            ["status"] = linked.Traversal.Status.ToString(),
            ["terminationReason"] = linked.Traversal.Reason,
            ["nodeCount"] = linked.Traversal.Nodes.Count,
            ["nodes"] = nodes,
            ["knownObjectCount"] = linked.KnownObjects.Count,
            ["resolvedCharacterBases"] = resolved,
            ["extraCharacterBases"] = extra
        };
    }

    private static Dictionary<string, object?> Capture(CharacterBase* parent, CharacterBase* child)
    {
        var attach = child->Attach;
        object? socket = null;
        object? transform = null;
        if (attach.AttachmentCount > 0)
        {
            var bone = attach.SkeletonBoneAttachments[0];
            socket = new Dictionary<string, object?>
            {
                ["name"] = null,
                ["partialSkeletonIndex"] = bone.BoneIndexMask.PartialSkeletonIdx,
                ["boneIndex"] = bone.BoneIndexMask.BoneIdx
            };
            var parsed = new Meddle.Plugin.Models.Transform(bone.ChildTransform);
            transform = Transform(parsed.Translation, parsed.Rotation, parsed.Scale);
        }

        return new Dictionary<string, object?>
        {
            ["parent"] = $"0x{(nint)parent:X}",
            ["child"] = $"0x{(nint)child:X}",
            ["socket"] = socket,
            ["transform"] = transform,
            ["executeType"] = attach.ExecuteType,
            ["reason"] = attach.AttachmentCount > 0 ? null : "runtime attachment has no bone attachment record"
        };
    }

    private static Dictionary<string, object?> Transform(Vector3 position, Quaternion rotation, Vector3 scale) => new()
    {
        ["translation"] = new[] { position.X, position.Y, position.Z },
        ["rotation"] = new[] { rotation.X, rotation.Y, rotation.Z, rotation.W },
        ["scale"] = new[] { scale.X, scale.Y, scale.Z }
    };
}
