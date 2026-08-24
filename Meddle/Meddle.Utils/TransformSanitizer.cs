using System.Numerics;

namespace Meddle.Utils;

public enum TransformSanitizeComponent
{
    None,
    Scale,
    Translation,
    Rotation
}

public readonly record struct TransformSanitizeIssue(
    TransformSanitizeComponent Component,
    string Reason,
    string Original);

public readonly record struct SanitizedTransform(
    Vector3 Scale,
    Quaternion Rotation,
    Vector3 Translation,
    IReadOnlyList<TransformSanitizeIssue> Issues)
{
    public bool WasModified => Issues.Count > 0;
}

/// <summary>
/// Canonical transform sanitization used by CharacterComposer, UE transform
/// conversion and debug/audit paths. The rules are intentionally centralized
/// here so callers never invent a second, subtly different fallback policy.
///
/// Rules:
/// <list type="bullet">
/// <item>non-finite scale -> one</item>
/// <item>near-zero scale component -> one (negative non-zero preserved)</item>
/// <item>non-finite translation -> zero</item>
/// <item>non-finite rotation -> identity</item>
/// <item>finite rotation with near-zero length -> identity</item>
/// <item>finite rotation with non-unit length -> normalized</item>
/// </list>
///
/// This type produces structured issues rather than logging directly so it can
/// stay free of a logging dependency; the plugin layer is responsible for
/// emitting warnings/observations from <see cref="SanitizedTransform.Issues"/>.
/// </summary>
public static class TransformSanitizer
{
    private const float ScaleEpsilon = 1e-6f;
    private const float RotationLengthSquaredEpsilon = 1e-12f;

    public static SanitizedTransform Sanitize(Vector3 scale, Quaternion rotation, Vector3 translation)
    {
        var issues = new List<TransformSanitizeIssue>();

        if (!TransformMath.IsFinite(scale))
        {
            issues.Add(new TransformSanitizeIssue(
                TransformSanitizeComponent.Scale,
                "NonFiniteScale",
                scale.ToString()));
            scale = Vector3.One;
        }
        else if (TransformMath.HasNearZeroScaleComponent(scale, ScaleEpsilon))
        {
            issues.Add(new TransformSanitizeIssue(
                TransformSanitizeComponent.Scale,
                "NearZeroScale",
                scale.ToString()));
            scale = Vector3.One;
        }

        if (!TransformMath.IsFinite(translation))
        {
            issues.Add(new TransformSanitizeIssue(
                TransformSanitizeComponent.Translation,
                "NonFiniteTranslation",
                translation.ToString()));
            translation = Vector3.Zero;
        }

        if (!TransformMath.IsFinite(rotation))
        {
            issues.Add(new TransformSanitizeIssue(
                TransformSanitizeComponent.Rotation,
                "NonFiniteRotation",
                rotation.ToString()));
            rotation = Quaternion.Identity;
        }
        else
        {
            var lengthSquared = rotation.LengthSquared();
            if (lengthSquared < RotationLengthSquaredEpsilon)
            {
                issues.Add(new TransformSanitizeIssue(
                    TransformSanitizeComponent.Rotation,
                    "NearZeroRotationLength",
                    rotation.ToString()));
                rotation = Quaternion.Identity;
            }
            else if (Math.Abs(lengthSquared - 1f) > 1e-4f)
            {
                issues.Add(new TransformSanitizeIssue(
                    TransformSanitizeComponent.Rotation,
                    "NonNormalizedRotation",
                    rotation.ToString()));
                rotation = Quaternion.Normalize(rotation);
            }
        }

        return new SanitizedTransform(scale, rotation, translation, issues);
    }
}
