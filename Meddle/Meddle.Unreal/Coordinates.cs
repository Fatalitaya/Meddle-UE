using System.Numerics;

namespace Meddle.Unreal;

public static class Coordinates
{
    public static Vector3 Direction(Vector3 v) => new(-v.Z, v.X, v.Y);
    public static Vector3 Position(Vector3 v) => Direction(v) * 100;
    private static readonly Matrix4x4 Basis = new(0, 1, 0, 0, 0, 0, 1, 0, -1, 0, 0, 0, 0, 0, 0, 1);
    public static BoneTransform Transform(Matrix4x4 matrix)
    {
        if (!Matrix4x4.Decompose(matrix, out var s, out var r, out var t))
            throw new ArgumentException("Bone transform cannot be decomposed.");
        var rotation = Quaternion.CreateFromRotationMatrix(Matrix4x4.Transpose(Basis) * Matrix4x4.CreateFromQuaternion(r) * Basis);
        return new(Position(t), Quaternion.Normalize(rotation), new(s.Z, s.X, s.Y));
    }
}
