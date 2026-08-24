using System.Numerics;
using SharpGLTF.Scenes;

namespace Meddle.Utils;

/// <summary>
/// Canonical owner-skeleton topology math shared by the production adapter
/// (<c>SkeletonUtils.ComputeBoneToRoot</c>) and by regression tests.
///
/// System.Numerics row-vector convention (points are <c>p * M</c>).
///
/// Contract:
/// <list type="bullet">
/// <item><c>BoneToRoot</c>: current-pose transform from a descendant bone to
/// the owning skeleton root local space. It EXCLUDES the root's own local
/// transform, so <c>ComputeBoneToRoot(rootBone) == Identity</c>.</item>
/// <item><c>RootWorld</c>: the actual world matrix of the skeleton root node,
/// including that root's own local transform (current pose, or child root
/// local/attach offset for attached characters).</item>
/// <item>A bone chain is computed strictly inside the owner skeleton's
/// identity set. The first ancestor that is not part of the owner set
/// terminates traversal; that foreign node is never included, never read for
/// its <c>LocalMatrix</c>, and never passed to the pose resolver.</item>
/// <item>Descendant composition:
/// <c>ChildRootWorld = childRootLocal * attachBoneToRoot * ownerRootWorld</c>
/// (see <see cref="AttachmentTransformPolicy"/>).</item>
/// </list>
/// </summary>
public static class SkeletonTopologyMath
{
    /// <summary>
    /// Collects the owned nodes from <paramref name="bone"/> up to (and
    /// including) the owner skeleton root, strictly within
    /// <paramref name="ownerBones"/> (reference identity boundary). Traversal
    /// stops at the first ancestor not present in the owner set; that foreign
    /// ancestor is never included.
    /// </summary>
    public static IReadOnlyList<BoneNodeBuilder> CollectOwnerChainNodes(
        BoneNodeBuilder bone,
        IReadOnlySet<BoneNodeBuilder> ownerBones)
    {
        var chain = new List<BoneNodeBuilder>();
        NodeBuilder? current = bone;
        while (current is BoneNodeBuilder boneNode && ownerBones.Contains(boneNode))
        {
            chain.Add(boneNode);
            current = boneNode.Parent;
        }

        return chain;
    }

    /// <summary>
    /// Resolves the current matrix of a single owned bone: current pose via
    /// <paramref name="poseResolver"/>, or <c>LocalMatrix</c> for generated
    /// bones and owned bones without a resolvable pose.
    /// </summary>
    public static Matrix4x4 ResolveBoneCurrentMatrix(
        BoneNodeBuilder bone,
        Func<BoneNodeBuilder, Matrix4x4?> poseResolver)
    {
        return bone.IsGenerated || poseResolver(bone) is not { } pose
            ? bone.LocalMatrix
            : pose;
    }

    /// <summary>
    /// Computes the transform that maps <paramref name="bone"/>'s local space
    /// into the owning skeleton root local space, using only the owner
    /// skeleton's own current pose and EXCLUDING the root's own local
    /// transform. Therefore:
    /// <code>
    /// ComputeBoneToRoot(rootBone) == Identity
    /// ComputeBoneToRoot(child)     == childLocal
    /// ComputeBoneToRoot(B)         == B.Local * A.Local   (root -> A -> B)
    /// </code>
    /// The returned matrix never contains foreign skeleton nodes. Combine it
    /// with the owner root world:
    /// <c>AttachBoneWorld = BoneToRoot * RootWorld</c>.
    /// </summary>
    public static Matrix4x4 ComputeBoneToRoot(
        BoneNodeBuilder bone,
        IReadOnlySet<BoneNodeBuilder> ownerBones,
        Func<BoneNodeBuilder, Matrix4x4?> poseResolver)
    {
        if (!ownerBones.Contains(bone))
        {
            throw new InvalidOperationException(
                $"Bone '{bone.BoneName}' does not belong to the owner skeleton; " +
                "refusing to compute an owner-root chain across a skeleton boundary.");
        }

        var nodes = CollectOwnerChainNodes(bone, ownerBones);
        var steps = new List<TransformMath.BoneChainStep>(nodes.Count);
        // The last node is the owner skeleton root; its own local transform
        // belongs to RootWorld (see ComputeRootWorld) and must not be applied
        // here, otherwise BoneToRoot * RootWorld would double-apply it.
        for (var i = 0; i < nodes.Count - 1; i++)
        {
            steps.Add(new TransformMath.BoneChainStep(
                true,
                ResolveBoneCurrentMatrix(nodes[i], poseResolver)));
        }

        return TransformMath.ComposeBoneChainSteps(steps);
    }

    /// <summary>
    /// Computes the actual world matrix of the owner skeleton root node:
    /// <code>
    /// RootWorld = rootLocal * rootParentWorld
    /// </code>
    /// where <c>rootLocal</c> is the root's current pose (or its
    /// LocalMatrix for generated roots) and <c>rootParentWorld</c> is the
    /// world matrix of the space the root is placed in (instance/world layer
    /// for a top-level character, or the attach point world of a parent).
    /// This is the ONLY value handed to descendants as their owner root world.
    /// </summary>
    public static Matrix4x4 ComputeRootWorld(
        BoneNodeBuilder rootBone,
        IReadOnlySet<BoneNodeBuilder> ownerBones,
        Func<BoneNodeBuilder, Matrix4x4?> poseResolver,
        Matrix4x4 rootParentWorld)
    {
        if (!ownerBones.Contains(rootBone))
        {
            throw new InvalidOperationException(
                $"Bone '{rootBone.BoneName}' does not belong to the owner skeleton; " +
                "refusing to compute a root world across a skeleton boundary.");
        }

        return TransformMath.ComposeWorld(
            ResolveBoneCurrentMatrix(rootBone, poseResolver),
            rootParentWorld);
    }
}
