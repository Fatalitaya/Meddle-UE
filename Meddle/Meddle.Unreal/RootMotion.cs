using System.Numerics;

namespace Meddle.Unreal;

public static class RootMotion
{
    /// <summary>Moves trajectory from a single character root onto Meddle_Root, preserving the local pose.</summary>
    public static BakedAnimation Extract(IReadOnlyList<AssetBone> bones, BakedAnimation animation, bool inPlace, bool relative = true)
    {
        var roots = Enumerable.Range(1, bones.Count - 1).Where(i => bones[i].Parent == 0).ToArray();
        if (roots.Length != 1) return animation;
        var track = animation.Tracks.Single(t => t.BoneIndex == roots[0]);
        var motion = animation.Tracks.Single(t => t.BoneIndex == 0);
        var reference = bones[roots[0]].ReferencePose;
        var start = track.Keys[0];
        for (var i = 0; i < track.Keys.Length; i++)
        {
            var key = track.Keys[i];
            var rotation = Quaternion.Normalize(key.Rotation * Quaternion.Inverse(relative ? start.Rotation : reference.Rotation));
            var origin = relative ? start.Translation : reference.Translation;
            var translation = key.Translation - origin + reference.Translation - Vector3.Transform(reference.Translation, rotation);
            motion.Keys[i] = inPlace ? BoneTransform.Identity : new(translation, rotation, Vector3.One);
            // Place the pose back at its reference origin and orientation. Root scale
            // remains on the character bone so race/height scaling is applied once.
            track.Keys[i] = new(reference.Translation, relative ? start.Rotation : reference.Rotation, key.Scale);
        }
        return animation;
    }
}
