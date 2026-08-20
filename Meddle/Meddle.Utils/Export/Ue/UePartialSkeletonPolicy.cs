namespace Meddle.Utils.Export.Ue;

public static class UePartialSkeletonPolicy
{
    public static bool IsEmptyRuntimeSlot(
        string? handlePath,
        bool hasHkSkeleton,
        uint boneCount,
        int poseCount,
        uint partialSkeletonFlagsRaw,
        int connectedBoneIndex)
    {
        return string.IsNullOrWhiteSpace(handlePath) &&
               !hasHkSkeleton &&
               boneCount == 0 &&
               poseCount == 0 &&
               partialSkeletonFlagsRaw == 0 &&
               connectedBoneIndex == -1;
    }

    public static bool HasRuntimeEvidenceWithoutSource(
        string? handlePath,
        bool hasHkSkeleton,
        uint boneCount,
        int poseCount)
    {
        return string.IsNullOrWhiteSpace(handlePath) &&
               (hasHkSkeleton || boneCount > 0 || poseCount > 0);
    }
}
