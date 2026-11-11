using System.Numerics;

namespace Meddle.Unreal;

// All asset data uses UE coordinates and centimetres. Game pointers never enter this layer.
public readonly record struct BoneTransform(Vector3 Translation, Quaternion Rotation, Vector3 Scale)
{
    public static BoneTransform Identity => new(Vector3.Zero, Quaternion.Identity, Vector3.One);
}

public sealed record AssetBone(string Name, int Parent, BoneTransform ReferencePose);
public sealed record AssetVertex(Vector3 Position, Vector3 Normal, Vector4 Tangent,
    Vector2[] TexCoords, uint Color, int[] Bones, float[] Weights);
public sealed record AssetSection(string MaterialSlot, string? MaterialPath, AssetVertex[] Vertices, uint[] Indices);
public sealed record MorphDelta(uint Vertex, Vector3 Position, Vector3 Normal);
public sealed record AssetMorph(string Name, MorphDelta[] Deltas, int[] Sections);
public sealed record AssetMesh(AssetBone[] Bones, AssetSection[] Sections, AssetMorph[]? Morphs = null);

internal static class AssetValidation
{
    public static void Bones(IReadOnlyList<AssetBone> bones)
    {
        if (bones.Count == 0) throw new ArgumentException("A skeleton needs at least one bone.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < bones.Count; i++)
        {
            var b = bones[i];
            if (string.IsNullOrWhiteSpace(b.Name) || !names.Add(b.Name))
                throw new ArgumentException("Bone names must be nonempty and unique.");
            if (b.Parent >= i || b.Parent < -1 || (i > 0 && b.Parent == -1))
                throw new ArgumentException("Skeletons require one root and parents before children.");
            Transform(b.ReferencePose);
        }
    }

    public static void Transform(BoneTransform t)
    {
        if (!Finite(t.Translation) || !Finite(t.Scale) || !float.IsFinite(t.Rotation.LengthSquared()) || t.Rotation.LengthSquared() < 1e-12f)
            throw new ArgumentException("Transforms must be finite with a nonzero rotation.");
    }

    private static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}
