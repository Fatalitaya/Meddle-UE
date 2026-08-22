using System.Numerics;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Graphics.Render;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using Meddle.Plugin.Models;
using Meddle.Utils.Audit;

namespace Meddle.Plugin.Audit.Collectors;

/// <summary>Captures observable weapon attachment endpoints and local transforms.</summary>
public sealed unsafe class AttachmentCollector : IRuntimeAuditCollector
{
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

            return new RuntimeAuditCollectorResult
            {
                Status = RuntimeAuditAvailability.Available,
                Observations = { AuditCollectorUtil.Observation(context, Category, "Attachment", new Dictionary<string, object?> { ["attachments"] = attachments }) }
            };
        }
        catch (Exception ex) { return AuditCollectorUtil.Failed(context, Category, "Attachment", ex); }
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
