using System.Numerics;

namespace Meddle.Utils;

/// <summary>
/// Verified transform composition helpers for the row-vector convention used
/// by <see cref="System.Numerics.Matrix4x4"/> and SharpGLTF in this project.
///
/// Convention: a point <c>p</c> is transformed by a matrix <c>M</c> as
/// <c>p * M</c>. The world matrix of a child is therefore:
///
/// <code>childWorld = childLocal * parentLocal * ... * rootLocal</code>
///
/// <see cref="ComposeWorld"/> takes locals ordered child-to-root and multiplies
/// them left-to-right. This is intentionally the inverse of the "root to leaf"
/// ordering used by some engines, and it is locked by the
/// <c>ComposeWorld_NonCommutingOrder</c> regression test.
/// </summary>
public static class TransformMath
{
    private const float ScaleEpsilon = 1e-6f;

    /// <summary>
    /// Compose a list of local matrices, ordered child-to-root, into a single
    /// world matrix using the row-vector convention:
    /// <c>local0 * local1 * ... * localN</c>.
    /// </summary>
    public static Matrix4x4 ComposeWorld(params Matrix4x4[] locals)
    {
        var result = Matrix4x4.Identity;
        foreach (var local in locals)
        {
            result *= local;
        }

        return result;
    }

    public readonly record struct BoneChainStep(bool Owned, Matrix4x4 Matrix);

    /// <summary>
    /// Compose a skeleton bone chain from provenance-carrying steps. Each step
    /// already resolved its matrix: owned skeleton bones carry their pose
    /// transform, generated/foreign nodes carry their scene-local matrix. The
    /// production adapter (<c>SkeletonUtils</c>) builds these steps; tests can
    /// build the same steps to exercise the exact production matrix ordering.
    /// </summary>
    public static Matrix4x4 ComposeBoneChainSteps(IReadOnlyList<BoneChainStep> childToRootSteps)
    {
        var result = Matrix4x4.Identity;
        foreach (var step in childToRootSteps)
        {
            result *= step.Matrix;
        }

        return result;
    }

    /// <summary>
    /// Skeleton-root detection based on the skeleton-native parent topology. A
    /// bone with parent index -1 is a root; this intentionally ignores the
    /// mutable export hierarchy, so reparenting a skeleton root under a mount
    /// bone does not change its identity as a root.
    /// </summary>
    public static bool IsSkeletonRootByParentIndex(int parentIndex)
    {
        return parentIndex == -1;
    }

    /// <summary>
    /// Convert a child world matrix to a local-to-parent matrix. Returns
    /// <c>false</c> when <paramref name="parentWorld"/> is not invertible; in
    /// that case <paramref name="local"/> is identity and callers must fail
    /// closed instead of writing a NaN matrix downstream.
    /// </summary>
    public static bool ComputeLocalFromWorld(
        Matrix4x4 childWorld,
        Matrix4x4 parentWorld,
        out Matrix4x4 local)
    {
        if (Matrix4x4.Invert(parentWorld, out var inverseParent))
        {
            local = childWorld * inverseParent;
            return IsFinite(local);
        }

        local = Matrix4x4.Identity;
        return false;
    }

    public static bool IsFinite(Matrix4x4 matrix)
    {
        return IsFinite(matrix.M11) && IsFinite(matrix.M12) && IsFinite(matrix.M13) && IsFinite(matrix.M14) &&
               IsFinite(matrix.M21) && IsFinite(matrix.M22) && IsFinite(matrix.M23) && IsFinite(matrix.M24) &&
               IsFinite(matrix.M31) && IsFinite(matrix.M32) && IsFinite(matrix.M33) && IsFinite(matrix.M34) &&
               IsFinite(matrix.M41) && IsFinite(matrix.M42) && IsFinite(matrix.M43) && IsFinite(matrix.M44);
    }

    public static bool IsAffine(Matrix4x4 matrix)
    {
        const float epsilon = 1e-4f;
        return IsFinite(matrix) &&
               Math.Abs(matrix.M14) < epsilon &&
               Math.Abs(matrix.M24) < epsilon &&
               Math.Abs(matrix.M34) < epsilon &&
               Math.Abs(matrix.M44 - 1f) < epsilon;
    }

    public static bool IsFinite(Vector3 value)
    {
        return IsFinite(value.X) && IsFinite(value.Y) && IsFinite(value.Z);
    }

    public static bool IsFinite(Quaternion value)
    {
        return IsFinite(value.X) && IsFinite(value.Y) && IsFinite(value.Z) && IsFinite(value.W);
    }

    public static bool IsFinite(float value)
    {
        return !(float.IsNaN(value) || float.IsInfinity(value));
    }

    /// <summary>
    /// A scale component is "near zero" when its absolute value is below the
    /// epsilon. Negative non-zero components are intentionally preserved;
    /// only true zero/near-zero magnitudes collapse to one.
    /// </summary>
    public static bool HasNearZeroScaleComponent(Vector3 scale, float epsilon = ScaleEpsilon)
    {
        return Math.Abs(scale.X) < epsilon ||
               Math.Abs(scale.Y) < epsilon ||
               Math.Abs(scale.Z) < epsilon;
    }
}
