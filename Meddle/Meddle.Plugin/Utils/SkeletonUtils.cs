using System.ComponentModel;
using System.Numerics;
using Meddle.Plugin.Models;
using Meddle.Plugin.Models.Skeletons;
using Meddle.Plugin.UI;
using Meddle.Utils;
using SharpGLTF.Scenes;
using SharpGLTF.Transforms;

namespace Meddle.Plugin.Utils;

public static class SkeletonUtils
{
    public enum PoseMode
    {
        [Description("Reference Pose")]
        None,
        [Description("Reference Pose with Scale")]
        LocalScaleOnly,
        [Description("Pose")]
        Local
    }
    
    public static (List<BoneNodeBuilder> List, BoneNodeBuilder Root)[] GetBoneMaps(
        ParsedSkeleton skeleton, PoseMode poseMode)
    {
        List<BoneNodeBuilder> boneMap = new();
        List<BoneNodeBuilder> rootList = new();

        for (var partialIdx = 0; partialIdx < skeleton.PartialSkeletons.Count; partialIdx++)
        {
            var partial = skeleton.PartialSkeletons[partialIdx];
            var hkSkeleton = partial.HkSkeleton;
            if (hkSkeleton == null)
                continue;

            var skeleBones = new BoneNodeBuilder[hkSkeleton.BoneNames.Count];
            for (var i = 0; i < hkSkeleton.BoneNames.Count; i++)
            {
                var name = hkSkeleton.BoneNames[i];
                if (string.IsNullOrEmpty(name))
                    continue;

                if (boneMap.FirstOrDefault(b => b.BoneName.Equals(name, StringComparison.OrdinalIgnoreCase)) is
                    { } dupeBone)
                {
                    skeleBones[i] = dupeBone;
                    continue;
                }

                // if (partial.ConnectedBoneIndex == i)
                // {
                //     throw new InvalidOperationException(
                //         $"Bone {name} on {i} is connected to a skeleton that should've already been declared");
                // }

                var bone = new BoneNodeBuilder(name)
                {
                    BoneIndex = i,
                    PartialSkeletonHandle = partial.HandlePath ??
                                            throw new InvalidOperationException(
                                                $"No handle path for {name} [{partialIdx},{i}]"),
                    PartialSkeletonIndex = partialIdx,
                };

                var boneTransform = hkSkeleton.ReferencePose[i].AffineTransform;
                bone.SetLocalTransform(boneTransform, false);

                var parentIdx = hkSkeleton.BoneParents[i];
                if (parentIdx != -1)
                {
                    skeleBones[parentIdx].AddNode(bone);
                }
                else
                {
                    rootList.Add(bone);
                }

                skeleBones[i] = bone;
                boneMap.Add(bone);
            }
        }

        // create separate lists based on each root
        var boneMapList = new List<(List<BoneNodeBuilder> List, BoneNodeBuilder Root)>();
        foreach (var root in rootList)
        {
            var bones = NodeBuilder.Flatten(root).Cast<BoneNodeBuilder>().ToList();
            if (!NodeBuilder.IsValidArmature(bones))
            {
                throw new InvalidOperationException($"Armature is invalid, {string.Join(", ", bones.Select(x => x.BoneName))}");
            }
            
            boneMapList.Add((bones, root));
        }

        var boneMaps = boneMapList.ToArray();

        // set pose scaling
        if (poseMode != PoseMode.None)
        {
            foreach (var map in boneMaps)
            {
                foreach (var bone in map.List)
                {
                    //ApplyPose(bone, skeleton, poseMode, 0);
                    var boneTransform = GetBoneTransform(skeleton, bone);
                    if (boneTransform == null)
                    {
                        continue;
                    }
    
                    AddBoneKeyframe(bone, poseMode, 0, boneTransform.Value);
                }
            }
        }

        return boneMaps;
    }
    

    public static List<BoneNodeBuilder> GetBoneMap(ParsedSkeleton skeleton, PoseMode poseMode, out BoneNodeBuilder? root)
    {
        var maps = GetBoneMaps(skeleton, poseMode);
        if (maps.Length == 0)
        {
            root = null;
            return [];
            //throw new InvalidOperationException("No roots were found");
        }

        // only known instance of this thus far is the Air-Wheeler A9 mount
        // contains two roots, one for the mount and an n_pluslayer which contains an additional skeleton
        var rootMap = maps.FirstOrDefault(x => x.Root.BoneName.Equals("n_root", StringComparison.OrdinalIgnoreCase));
        if (rootMap != default)
        {
            root = rootMap.Root;
            return rootMap.List;
        }
        
        var map0 = maps[0];
        root = map0.Root;
        return map0.List;
    }

    public record AttachGrouping(List<BoneNodeBuilder> Bones, BoneNodeBuilder? Root, List<(DateTime Time, AttachSet Attach)> Timeline);
    public static Dictionary<string, AttachGrouping> GetAnimatedBoneMap((DateTime Time, AttachSet[] Attaches)[] frames, AnimationExportSettings settings)
    {
        var attachDict = new Dictionary<string, AttachGrouping>();
        var attachTimelines = new Dictionary<string, List<(DateTime Time, AttachSet Attach)>>();
        foreach (var frame in frames)
        {
            foreach (var attach in frame.Attaches)
            {
                var timelineName = $"{attach.Id}_{attach.Name}";
                if (!attachTimelines.TryGetValue(timelineName, out var timeline))
                {
                    timeline = [];
                    attachTimelines.Add(timelineName, timeline);
                }

                timeline.Add((frame.Time, attach));
            }
        }

        var startTime = frames.Min(x => x.Time);
        foreach (var (attachId, timeline) in attachTimelines)
        {
            ProcessTimeline(attachId, timeline, attachDict, startTime, settings);
        }

        return attachDict;
    }

    private static void ProcessTimeline(
        string attachId,
        List<(DateTime Time, AttachSet Attach)> timeline,
        Dictionary<string, AttachGrouping> attachDict,
        DateTime startTime,
        AnimationExportSettings settings)
    {
        if (!attachDict.TryGetValue(attachId, out var attachBoneMap))
        {
            attachBoneMap = new AttachGrouping([], null, timeline);
            attachDict.Add(attachId, attachBoneMap);
        }

        if (timeline.Count == 0)
        {
            return; // no frames for this attach
        }

        // A recording must keep one stable skeleton topology.
        string[]? topology = null;
        foreach (var time in timeline.Select(x => x.Time).Distinct())
        {
            var frame = timeline.FirstOrDefault(x => x.Time == time);
            var frameTime = TotalSeconds(frame.Time, startTime);

            // get the bone map for this attach
            var boneMap = GetBoneMap(frame.Attach.Skeleton, PoseMode.None, out var attachRoot);
            var currentTopology = boneMap.Select(b => $"{b.BoneName}|{b.Parent?.Name}").ToArray();
            topology ??= currentTopology;
            if (!topology.SequenceEqual(currentTopology))
                throw new InvalidOperationException("Skeleton changed during capture. Record each topology separately.");
            if (attachRoot == null)
                continue;

            if (attachBoneMap.Root == null)
            {
                attachBoneMap = attachBoneMap with { Root = attachRoot };
            }

            // for each bone in the bone map
            foreach (var attachBone in boneMap)
            {
                // get the transform for this bone
                var currentTransform = GetBoneTransform(frame.Attach.Skeleton, attachBone);
                if (currentTransform == null)
                    continue; // no transform for this bone

                // find the bone in the attachBoneMap
                var bone = attachBoneMap.Bones.FirstOrDefault(x => x.BoneName.Equals(attachBone.BoneName, StringComparison.OrdinalIgnoreCase));
                if (bone == null)
                {
                    attachBoneMap.Bones.Add(attachBone);
                    bone = attachBone;
                }

                // Preserve every captured final pose. Dropping constant samples can turn
                // a held pose into a ramp when the next sample changes.
                AddBoneKeyframe(bone, PoseMode.Local, frameTime, currentTransform.Value);
            }
            attachDict[attachId] = attachBoneMap;
        }
    }
    
    private static void AddBoneKeyframe(BoneNodeBuilder bone, PoseMode poseMode, float time, AffineTransform transform)
    {
        bone.UseScale().UseTrackBuilder("pose").WithPoint(time, transform.Scale);
        
        if (poseMode != PoseMode.LocalScaleOnly)
        {
            bone.UseRotation().UseTrackBuilder("pose").WithPoint(time, transform.Rotation);
            bone.UseTranslation().UseTrackBuilder("pose").WithPoint(time, transform.Translation);
        }
    }

    public static Matrix4x4 ToMatrix(AffineTransform t) =>
        Matrix4x4.CreateScale(t.Scale) *
        Matrix4x4.CreateFromQuaternion(t.Rotation) *
        Matrix4x4.CreateTranslation(t.Translation);

    /// <summary>
    /// Production adapter over
    /// <see cref="SkeletonTopologyMath.ComputeBoneToRoot"/>. Computes the
    /// transform that maps <paramref name="bone"/>'s local space into the owner
    /// skeleton root's local space (bone-local -&gt; owner-root space) using ONLY
    /// the owner skeleton's current pose, and EXCLUDING the owner root's own
    /// local transform (<c>ComputeBoneToRoot(rootBone) == Identity</c>).
    ///
    /// Contract:
    /// <list type="bullet">
    /// <item><paramref name="bone"/> must belong to
    /// <paramref name="ownerBones"/> (reference identity boundary).</item>
    /// <item>Traversal stops at the first ancestor outside the owner set;
    /// foreign nodes are never included, never read for their LocalMatrix, and
    /// never resolved through this skeleton's pose data.</item>
    /// <item>The result is owner-skeleton pose only and excludes the root's
    /// own local transform. Combine it with the owner root's ACTUAL world
    /// matrix (see <see cref="ComputeRootWorld"/>) through
    /// <see cref="AttachmentTransformPolicy.ComposeAttachedRootWorld"/>.</item>
    /// </list>
    /// </summary>
    public static Matrix4x4 ComputeBoneToRoot(
        BoneNodeBuilder bone,
        ParsedSkeleton ownerSkeleton,
        IReadOnlySet<BoneNodeBuilder> ownerBones)
    {
        return SkeletonTopologyMath.ComputeBoneToRoot(
            bone,
            ownerBones,
            current => ResolvePoseOrNull(ownerSkeleton, current));
    }

    /// <summary>
    /// Production adapter over
    /// <see cref="SkeletonTopologyMath.ComputeRootWorld"/>. Computes the actual
    /// world matrix of the owner skeleton root node:
    /// <c>RootWorld = rootLocal * rootParentWorld</c>, where <c>rootLocal</c>
    /// is the root's current pose (including skeleton-native root scale) and
    /// <c>rootParentWorld</c> is the instance/world layer for a top-level
    /// character or the attach point world of a parent. This is the ONLY value
    /// handed to descendants as their owner root world.
    /// </summary>
    public static Matrix4x4 ComputeRootWorld(
        BoneNodeBuilder rootBone,
        ParsedSkeleton ownerSkeleton,
        IReadOnlySet<BoneNodeBuilder> ownerBones,
        Matrix4x4 rootParentWorld)
    {
        return SkeletonTopologyMath.ComputeRootWorld(
            rootBone,
            ownerBones,
            current => ResolvePoseOrNull(ownerSkeleton, current),
            rootParentWorld);
    }

    private static Matrix4x4? ResolvePoseOrNull(ParsedSkeleton ownerSkeleton, BoneNodeBuilder bone)
    {
        return GetBoneTransform(ownerSkeleton, bone) is { } poseTransform
            ? ToMatrix(poseTransform)
            : (Matrix4x4?)null;
    }
    
    public static AffineTransform? GetBoneTransform(ParsedSkeleton skeleton, BoneNodeBuilder bone)
    {
        if (bone.PartialSkeletonIndex < 0 || bone.PartialSkeletonIndex >= skeleton.PartialSkeletons.Count)
        {
            return null;
        }

        var partial = skeleton.PartialSkeletons[bone.PartialSkeletonIndex];
        if (partial.Poses.Count == 0) return null;

        if (bone.BoneIndex < 0 || bone.BoneIndex >= partial.Poses[0].Pose.Count)
        {
            return null;
        }

        var pose = partial.Poses[0];
        var boneTransform = pose.Pose[bone.BoneIndex].AffineTransform;

        // Root scale comes from the skeleton-native topology, never from the
        // mutable export hierarchy (a reparented player root must still be
        // recognized as the player skeleton root).
        if (IsSkeletonRoot(partial, bone))
        {
            var scale = boneTransform.Scale * skeleton.Transform.Scale;
            return new AffineTransform(scale, boneTransform.Rotation, boneTransform.Translation);
        }
        
        return boneTransform;
    }

    private static bool IsSkeletonRoot(ParsedPartialSkeleton partial, BoneNodeBuilder bone)
    {
        var hk = partial.HkSkeleton;
        if (hk == null || bone.BoneIndex < 0 || bone.BoneIndex >= hk.BoneParents.Count)
        {
            return false;
        }

        return TransformMath.IsSkeletonRootByParentIndex(hk.BoneParents[bone.BoneIndex]);
    }

    public static bool TryResolveAttachPoint(
        ParsedSkeleton ownerSkeleton,
        IReadOnlyList<BoneNodeBuilder> ownerBones,
        ParsedAttach attach,
        out BoneNodeBuilder? attachBone,
        out string? attachBoneName,
        out string? reason)
    {
        attachBone = null;
        attachBoneName = null;

        if (attach.PartialSkeletonIdx < 0 || attach.PartialSkeletonIdx >= ownerSkeleton.PartialSkeletons.Count)
        {
            reason = "partial skeleton index out of range";
            return false;
        }

        var partial = ownerSkeleton.PartialSkeletons[attach.PartialSkeletonIdx];
        var hk = partial.HkSkeleton;
        if (hk == null)
        {
            reason = "owner partial skeleton has no HkSkeleton";
            return false;
        }

        if (attach.BoneIdx < 0 || attach.BoneIdx >= hk.BoneNames.Count)
        {
            reason = "bone index out of range";
            return false;
        }

        var boneName = hk.BoneNames[(int)attach.BoneIdx];
        if (string.IsNullOrEmpty(boneName))
        {
            reason = "attach bone name is null or empty";
            return false;
        }

        attachBone = ownerBones.FirstOrDefault(
            x => x.BoneName.Equals(boneName, StringComparison.Ordinal));
        if (attachBone == null)
        {
            reason = $"attach bone {boneName} was not found in the owner bone map";
            attachBoneName = boneName;
            return false;
        }

        attachBoneName = boneName;
        reason = null;
        return true;
    }
    
    public static float TotalSeconds(DateTime time, DateTime startTime)
    {
        var seconds = (float)(time - startTime).TotalSeconds;
        return seconds < 0.0001f ? 0f : seconds;
    }
}
