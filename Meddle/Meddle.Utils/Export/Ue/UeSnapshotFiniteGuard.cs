namespace Meddle.Utils.Export.Ue;

/// <summary>
/// Recursive pre-serialization guard over every transform array in a
/// <see cref="UeCharacterSnapshot"/>. The UE JSON serializer keeps
/// <c>AllowNamedFloatingPointLiterals</c> enabled, so a NaN/Infinity that
/// slipped through sanitization would be written as a named literal; this guard
/// makes that impossible by failing before serialization.
/// </summary>
public static class UeSnapshotFiniteGuard
{
    public static IReadOnlyList<string> FindNonFiniteTransforms(UeCharacterSnapshot snapshot)
    {
        var issues = new List<string>();
        if (snapshot.Root != null)
        {
            WalkNode(snapshot.Root, "root", issues);
        }

        return issues;
    }

    public static bool AreAllTransformsFinite(UeCharacterSnapshot snapshot)
    {
        return FindNonFiniteTransforms(snapshot).Count == 0;
    }

    private static void WalkNode(UeCharacterNodeSnapshot node, string path, List<string> issues)
    {
        if (node.Skeleton != null)
        {
            WalkSkeleton(node.Skeleton, $"{path}.skeleton", issues);
        }

        if (node.Attach != null)
        {
            WalkAttach(node.Attach, $"{path}.attach", issues);
        }

        for (var i = 0; i < node.Attaches.Count; i++)
        {
            WalkNode(node.Attaches[i], $"{path}.attaches[{i}]", issues);
        }
    }

    private static void WalkSkeleton(UeSkeletonSnapshot skeleton, string path, List<string> issues)
    {
        CheckTransform(skeleton.Transform, $"{path}.transform", issues);
        for (var i = 0; i < skeleton.PartialSkeletons.Count; i++)
        {
            var partial = skeleton.PartialSkeletons[i];
            var partialPath = $"{path}.partialSkeletons[{i}]";
            for (var p = 0; p < partial.RuntimePoses.Count; p++)
            {
                var pose = partial.RuntimePoses[p];
                for (var t = 0; t < pose.Transforms.Count; t++)
                {
                    CheckTransform(pose.Transforms[t], $"{partialPath}.runtimePoses[{p}].transforms[{t}]", issues);
                }
            }
        }
    }

    private static void WalkAttach(UeAttachSnapshot attach, string path, List<string> issues)
    {
        CheckTransform(attach.OffsetTransform, $"{path}.offsetTransform", issues);
        if (attach.OwnerSkeleton != null)
        {
            WalkSkeleton(attach.OwnerSkeleton, $"{path}.ownerSkeleton", issues);
        }

        if (attach.TargetSkeleton != null)
        {
            WalkSkeleton(attach.TargetSkeleton, $"{path}.targetSkeleton", issues);
        }
    }

    private static void CheckTransform(UeTransformSnapshot? transform, string path, List<string> issues)
    {
        if (transform == null)
        {
            return;
        }

        CheckArray(transform.Translation, $"{path}.translation", issues);
        CheckArray(transform.Rotation, $"{path}.rotation", issues);
        CheckArray(transform.Scale, $"{path}.scale", issues);
    }

    private static void CheckArray(float[] values, string path, List<string> issues)
    {
        for (var i = 0; i < values.Length; i++)
        {
            if (!float.IsFinite(values[i]))
            {
                issues.Add($"{path}[{i}]");
            }
        }
    }
}
