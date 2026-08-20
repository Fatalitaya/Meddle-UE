using Meddle.Utils.Export.Ue;

namespace Meddle.Utils.Tests;

[TestClass]
public sealed class UePartialSkeletonPolicyTests
{
    [TestMethod]
    public void EmptyRuntimeSlot_IsEmpty_WhenAllSkeletonContentIsAbsent()
    {
        Assert.IsTrue(UePartialSkeletonPolicy.IsEmptyRuntimeSlot(
            handlePath: null,
            hasHkSkeleton: false,
            boneCount: 0,
            poseCount: 0,
            partialSkeletonFlagsRaw: 0,
            connectedBoneIndex: -1));
    }

    [TestMethod]
    public void EmptyRuntimeSlot_IgnoresWhitespaceHandlePath()
    {
        Assert.IsTrue(UePartialSkeletonPolicy.IsEmptyRuntimeSlot(
            handlePath: " ",
            hasHkSkeleton: false,
            boneCount: 0,
            poseCount: 0,
            partialSkeletonFlagsRaw: 0,
            connectedBoneIndex: -1));
    }

    [TestMethod]
    public void EmptyRuntimeSlot_IsNotEmpty_WhenHandlePathExists()
    {
        Assert.IsFalse(UePartialSkeletonPolicy.IsEmptyRuntimeSlot(
            handlePath: "chara/empty.sklb",
            hasHkSkeleton: false,
            boneCount: 0,
            poseCount: 0,
            partialSkeletonFlagsRaw: 0,
            connectedBoneIndex: -1));
    }

    [TestMethod]
    public void EmptyRuntimeSlot_IsNotEmpty_WhenHkSkeletonExists()
    {
        Assert.IsFalse(UePartialSkeletonPolicy.IsEmptyRuntimeSlot(
            handlePath: null,
            hasHkSkeleton: true,
            boneCount: 0,
            poseCount: 0,
            partialSkeletonFlagsRaw: 0,
            connectedBoneIndex: -1));
    }

    [TestMethod]
    public void EmptyRuntimeSlot_IsNotEmpty_WhenBoneCountIsPositive()
    {
        Assert.IsFalse(UePartialSkeletonPolicy.IsEmptyRuntimeSlot(
            handlePath: null,
            hasHkSkeleton: false,
            boneCount: 1,
            poseCount: 0,
            partialSkeletonFlagsRaw: 0,
            connectedBoneIndex: -1));
    }

    [TestMethod]
    public void EmptyRuntimeSlot_IsNotEmpty_WhenPosesExist()
    {
        Assert.IsFalse(UePartialSkeletonPolicy.IsEmptyRuntimeSlot(
            handlePath: null,
            hasHkSkeleton: false,
            boneCount: 0,
            poseCount: 1,
            partialSkeletonFlagsRaw: 0,
            connectedBoneIndex: -1));
    }

    [TestMethod]
    public void EmptyRuntimeSlot_IsNotEmpty_WhenFlagsAreNonZero()
    {
        Assert.IsFalse(UePartialSkeletonPolicy.IsEmptyRuntimeSlot(
            handlePath: null,
            hasHkSkeleton: false,
            boneCount: 0,
            poseCount: 0,
            partialSkeletonFlagsRaw: 1,
            connectedBoneIndex: -1));
    }

    [TestMethod]
    public void EmptyRuntimeSlot_IsNotEmpty_WhenConnectedBoneIndexIsValid()
    {
        Assert.IsFalse(UePartialSkeletonPolicy.IsEmptyRuntimeSlot(
            handlePath: null,
            hasHkSkeleton: false,
            boneCount: 0,
            poseCount: 0,
            partialSkeletonFlagsRaw: 0,
            connectedBoneIndex: 0));
    }

    [TestMethod]
    public void RuntimeEvidenceWithoutSource_DoesNotImplySklbOwnership()
    {
        Assert.IsFalse(UePartialSkeletonPolicy.HasRuntimeEvidenceWithoutSource(
            handlePath: null,
            hasHkSkeleton: false,
            boneCount: 0,
            poseCount: 0));
        Assert.IsTrue(UePartialSkeletonPolicy.HasRuntimeEvidenceWithoutSource(
            handlePath: null,
            hasHkSkeleton: true,
            boneCount: 0,
            poseCount: 0));
        Assert.IsTrue(UePartialSkeletonPolicy.HasRuntimeEvidenceWithoutSource(
            handlePath: null,
            hasHkSkeleton: false,
            boneCount: 1,
            poseCount: 0));
        Assert.IsTrue(UePartialSkeletonPolicy.HasRuntimeEvidenceWithoutSource(
            handlePath: null,
            hasHkSkeleton: false,
            boneCount: 0,
            poseCount: 1));
    }
}
