using FFXIVClientStructs.FFXIV.Client.Graphics.Render;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using Meddle.Plugin.Models;
using Meddle.Plugin.Models.Structs;
using Meddle.Plugin.Utils;
using Meddle.Utils.Audit;
using CSCharacter = FFXIVClientStructs.FFXIV.Client.Game.Character.Character;

namespace Meddle.Plugin.Audit;

/// <summary>
/// Native adapter and resolver for Phase B linked-attach evidence. This layer
/// owns the unsafe reads; the traversal algorithm itself is pure and lives in
/// <see cref="LinkedAttachTraversal"/>.
/// </summary>
internal static unsafe class LinkedAttachEvidence
{
    internal sealed record ResolvedCharacterBase(nint Address, string Classification, int ExecuteType);

    internal sealed record Capture(
        LinkedAttachTraversalResult Traversal,
        IReadOnlySet<nint> KnownObjects,
        IReadOnlyList<ResolvedCharacterBase> ResolvedCharacterBases);

    public static Capture CaptureEvidence(CSCharacter* character, CharacterBase* owner, SigUtil sigUtil)
    {
        var known = BuildKnownObjects(character, owner);
        var traversal = Traverse(owner, sigUtil);
        var resolved = Resolve(traversal, owner, known);
        return new Capture(traversal, known, resolved);
    }

    public static LinkedAttachTraversalResult Traverse(CharacterBase* owner, SigUtil sigUtil)
    {
        if (owner == null)
        {
            return new LinkedAttachTraversalResult(
                LinkedAttachTraversalStatus.Unavailable,
                [],
                "owner CharacterBase is null");
        }

        if (!sigUtil.TryGetAttachVTable(out var vtable))
        {
            return new LinkedAttachTraversalResult(
                LinkedAttachTraversalStatus.Unavailable,
                [],
                "attach vtable signature unavailable");
        }

        var startAddress = (nint)(&owner->Attach);
        return LinkedAttachTraversal.Traverse(startAddress, vtable, new NativeAttachNodeReader());
    }

    public static IReadOnlySet<nint> BuildKnownObjects(CSCharacter* character, CharacterBase* owner)
    {
        if (character == null)
        {
            return LinkedAttachKnownSet.Build(0, (nint)owner, [], 0, 0, 0);
        }

        var weaponDrawObjects = new List<nint>();
        foreach (var weapon in character->DrawData.WeaponData)
        {
            // Phase A: weapon existence comes from DrawData.DrawObject only.
            // IsHidden must not be used to decide whether a weapon exists.
            var drawObject = weapon.DrawData.DrawObject;
            if (drawObject != null)
            {
                weaponDrawObjects.Add((nint)drawObject);
            }
        }

        return LinkedAttachKnownSet.Build(
            (nint)character,
            (nint)owner,
            weaponDrawObjects,
            character->Mount.MountObject != null ? (nint)character->Mount.MountObject->DrawObject : 0,
            character->CompanionData.CompanionObject != null ? (nint)character->CompanionData.CompanionObject->DrawObject : 0,
            character->OrnamentData.OrnamentObject != null ? (nint)character->OrnamentData.OrnamentObject->DrawObject : 0);
    }

    public static IReadOnlyList<ResolvedCharacterBase> Resolve(
        LinkedAttachTraversalResult traversal,
        CharacterBase* owner,
        IReadOnlySet<nint> known)
    {
        var resolved = new Dictionary<nint, ResolvedCharacterBase>();
        foreach (var node in traversal.Nodes)
        {
            var child = ResolveChild(node, owner);
            if (child == 0 || child == (nint)owner)
            {
                continue;
            }

            var classification = known.Contains(child) ? "known" : "extra";
            if (!resolved.ContainsKey(child))
            {
                resolved[child] = new ResolvedCharacterBase(child, classification, node.ExecuteType);
            }
        }

        return resolved.Values.OrderBy(x => x.Address).ToArray();
    }

    private static nint ResolveChild(AttachNodeData node, CharacterBase* owner)
    {
        if (owner == null)
        {
            return 0;
        }

        bool attachedToOwner;
        switch (node.ExecuteType)
        {
            case 3:
                attachedToOwner = node.OwnerUnion != 0 && (CharacterBase*)node.OwnerUnion == owner;
                break;
            case 4:
                attachedToOwner = node.OwnerUnion != 0 && (Skeleton*)node.OwnerUnion == owner->Skeleton;
                break;
            default:
                attachedToOwner = false;
                break;
        }

        if (!attachedToOwner || node.TargetSkeleton == 0)
        {
            return 0;
        }

        var child = ((Skeleton*)node.TargetSkeleton)->Owner;
        return child == null ? 0 : (nint)child;
    }

    private sealed class NativeAttachNodeReader : IAttachNodeReader
    {
        public bool TryReadVTable(nint address, out nint vtable)
        {
            if (address == 0)
            {
                vtable = 0;
                return false;
            }

            var attach = (MeddleAttach*)address;
            vtable = (nint)attach->VTable;
            return true;
        }

        public bool TryReadPayload(nint address, nint vtable, out AttachNodeData node)
        {
            if (address == 0)
            {
                node = default;
                return false;
            }

            var attach = (MeddleAttach*)address;
            node = new AttachNodeData(
                address,
                vtable,
                (nint)attach->LinkedListPrevious,
                (nint)attach->LinkedListNext,
                attach->ExecuteType,
                (nint)attach->TargetSkeleton,
                (nint)attach->OwnerUnion,
                attach->AttachmentCount,
                (nint)attach->SkeletonBoneAttachments);
            return true;
        }
    }
}
