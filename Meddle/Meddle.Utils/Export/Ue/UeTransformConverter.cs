using System.Numerics;

namespace Meddle.Utils.Export.Ue;

/// <summary>
/// Canonical conversion from a raw scale/rotation/translation transform into a
/// <see cref="UeTransformSnapshot"/>. This is the single production function
/// behind <c>UeSnapshotBuilder.ToTransform</c> and the regression tests, so the
/// sanitizer argument mapping (scale, rotation, translation) can never drift
/// again.
///
/// The sanitizer contract is
/// <c>TransformSanitizer.Sanitize(scale: ..., rotation: ..., translation: ...)</c>;
/// the call here uses named arguments because Scale and Translation are both
/// <see cref="Vector3"/> and positional calls are not type-safe.
/// </summary>
public static class UeTransformConverter
{
    public static UeTransformSnapshot Convert(
        Vector3 scale,
        Quaternion rotation,
        Vector3 translation,
        Action<TransformSanitizeIssue>? onIssue = null)
    {
        var sanitized = TransformSanitizer.Sanitize(
            scale: scale,
            rotation: rotation,
            translation: translation);

        foreach (var issue in sanitized.Issues)
        {
            onIssue?.Invoke(issue);
        }

        return new UeTransformSnapshot
        {
            Translation = AsFloatArray(sanitized.Translation),
            Rotation = AsFloatArray(sanitized.Rotation),
            Scale = AsFloatArray(sanitized.Scale)
        };
    }

    private static float[] AsFloatArray(Vector3 value)
    {
        return [value.X, value.Y, value.Z];
    }

    private static float[] AsFloatArray(Quaternion value)
    {
        return [value.X, value.Y, value.Z, value.W];
    }
}
