using System.Numerics;

namespace Meddle.Utils;

/// <summary>
/// Canonical attachment composition used by <c>CharacterComposer</c> (via
/// <c>SkeletonUtils.ComputeBoneToRoot</c> / <c>ComputeRootWorld</c>) and by
/// regression tests.
///
/// System.Numerics row-vector convention (points are <c>p * M</c>):
/// <code>
/// AttachBoneWorld = attachBoneToRoot * ownerRootWorld
/// ChildRootWorld  = childRootLocal * attachBoneToRoot * ownerRootWorld
/// </code>
/// where
/// <list type="bullet">
/// <item><c>childRootLocal</c> is the child skeleton root's scene-local matrix
/// (attach offset applied exactly once, into this matrix).</item>
/// <item><c>attachBoneToRoot</c> is the owner-skeleton-only current pose chain
/// from the attach bone to the owner skeleton root local space, EXCLUDING the
/// owner root's own local transform
/// (<c>ComputeBoneToRoot(root) == Identity</c>).</item>
/// <item><c>ownerRootWorld</c> is the ACTUAL world matrix of the owner skeleton
/// root node (including its own local transform), i.e. the value produced by
/// <c>ComputeRootWorld</c> / the parent compose result's <c>RootWorld</c>. It
/// is propagated explicitly and must never be re-derived by walking scene
/// parents through foreign skeletons.</item>
/// </list>
///
/// The child's <c>ChildRootWorld</c> becomes the next owner's
/// <c>ownerRootWorld</c>, so nested hierarchies compose naturally without a
/// second "attach point world" concept.
/// </summary>
public static class AttachmentTransformPolicy
{
    public static Matrix4x4 ComposeAttachedRootWorld(
        Matrix4x4 childRootLocal,
        Matrix4x4 attachBoneToRoot,
        Matrix4x4 ownerRootWorld)
    {
        return TransformMath.ComposeWorld(childRootLocal, attachBoneToRoot, ownerRootWorld);
    }
}
