using System.Numerics;
using UAssetAPI;

namespace Meddle.Unreal;

public sealed record BoneTrack(int BoneIndex, BoneTransform[] Keys);
public sealed record BakedAnimation(int FramesPerSecond, int FrameCount, BoneTrack[] Tracks);

public static class AnimationWriter
{
    public static void Write(string file, string packagePath, string skeletonPath, Guid skeletonGuid,
        IReadOnlyList<AssetBone> bones, BakedAnimation animation, bool rootMotion = false)
    {
        AssetValidation.Bones(bones);
        if (animation.FramesPerSecond <= 0 || animation.FrameCount < 1 || animation.Tracks.Length == 0 ||
            animation.Tracks.Select(t => t.BoneIndex).Distinct().Count() != animation.Tracks.Length)
            throw new ArgumentException("Animation needs a positive rate and unique nonempty tracks.");
        foreach (var t in animation.Tracks)
        {
            if (t.BoneIndex < 0 || t.BoneIndex >= bones.Count || t.Keys.Length != animation.FrameCount + 1)
                throw new ArgumentException("Animation tracks must match the skeleton and frame count.");
            foreach (var key in t.Keys) AssetValidation.Transform(key);
        }
        var p = new AssetPackage(packagePath);
        var skeleton = p.Reference(skeletonPath, "Skeleton");
        p.AddObject(p.Name, "AnimSequence", w =>
        {
            p.ObjectProperty(w, "Skeleton", skeleton);
            p.BoolProperty(w, "bEnableRootMotion", rootMotion);
            p.Property(w, "DataModelInterface", p.Bytes(b => b.Write(2)), ("InterfaceProperty", 0));
            p.Property(w, "SequenceLength", p.Bytes(b => b.Write((float)animation.FrameCount / animation.FramesPerSecond)), ("FloatProperty", 0));
        }, p.Bytes(w => { w.Write(skeletonGuid); w.Write((ushort)0); w.Write(0); w.Write(0); }));
        p.AddObject("AnimationDataModel", "AnimDataModel", w =>
        {
            p.IntProperty(w, "NumberOfFrames", animation.FrameCount);
            p.IntProperty(w, "NumberOfKeys", animation.FrameCount + 1);
            p.Property(w, "FrameRate", p.Bytes(b =>
            {
                p.IntProperty(b, "Numerator", animation.FramesPerSecond);
                p.IntProperty(b, "Denominator", 1); p.End(b);
            }), ("StructProperty", 1), ("FrameRate", 1), ("/Script/CoreUObject", 0));
            // BoolProperty carries its value in the property tag flags (0x10).
            w.Write(p.NameOf("bPopulated")); w.Write(p.NameOf("BoolProperty")); w.Write(0); w.Write(0); w.Write((byte)0x10);
            p.Property(w, "BoneAnimationTracks", p.Bytes(b =>
            {
                b.Write(animation.Tracks.Length);
                foreach (var track in animation.Tracks)
                {
                    p.IntProperty(b, "BoneTreeIndex", track.BoneIndex);
                    p.Property(b, "Name", p.Bytes(v => v.Write(p.NameOf(bones[track.BoneIndex].Name))), ("NameProperty", 0));
                    p.NativeProperty(b, "InternalTrackData", p.Bytes(v =>
                    {
                        VectorKeys(v, track.Keys.Select(k => k.Translation));
                        v.Write(16); v.Write(track.Keys.Length);
                        Quaternion? previous = null;
                        foreach (var key in track.Keys)
                        {
                            var q = Quaternion.Normalize(key.Rotation);
                            if (previous.HasValue && Quaternion.Dot(previous.Value, q) < 0) q = -q;
                            previous = q;
                            v.Write(q.X); v.Write(q.Y); v.Write(q.Z); v.Write(q.W);
                        }
                        VectorKeys(v, track.Keys.Select(k => k.Scale));
                    }), ("StructProperty", 1), ("RawAnimSequenceTrack", 1), ("/Script/Engine", 0));
                    p.End(b);
                }
            }), ("ArrayProperty", 1), ("StructProperty", 1), ("BoneAnimationTrack", 1), ("/Script/Engine", 0));
        }, [], outer: 1);
        p.Save(file);
    }

    private static void VectorKeys(AssetBinaryWriter w, IEnumerable<Vector3> values)
    {
        var array = values.ToArray();
        w.Write(12); w.Write(array.Length);
        foreach (var v in array) { w.Write(v.X); w.Write(v.Y); w.Write(v.Z); }
    }
}
