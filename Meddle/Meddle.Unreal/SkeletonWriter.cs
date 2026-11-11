using System.Numerics;
using UAssetAPI;
using UAssetAPI.UnrealTypes;

namespace Meddle.Unreal;

public static class SkeletonWriter
{
    public static Guid Write(string file, string packagePath, IReadOnlyList<AssetBone> bones, Guid? identity = null)
    {
        AssetValidation.Bones(bones);
        var p = new AssetPackage(packagePath);
        var guid = identity ?? Guid.NewGuid();
        var native = p.Bytes(w =>
        {
            ReferenceSkeleton(p, w, bones);
            w.Write(0); // retarget sources
            w.Write(guid);
            w.Write((ushort)0); // strip flags
            w.Write(0); // marker names
        });
        p.AddObject(p.Name, "Skeleton", w =>
        {
            var tree = p.Bytes(b =>
            {
                b.Write(bones.Count);
                foreach (var bone in bones)
                {
                    p.Property(b, "TranslationRetargetingMode", p.Bytes(v => v.Write(p.NameOf("EBoneTranslationRetargetingMode::Animation"))),
                        ("ByteProperty", 1), ("EBoneTranslationRetargetingMode", 1), ("/Script/Engine", 0));
                    p.End(b);
                }
            });
            p.Property(w, "BoneTree", tree, ("ArrayProperty", 1), ("StructProperty", 1), ("BoneNode", 1), ("/Script/Engine", 0));
        }, native);
        p.Save(file);
        return guid;
    }

    internal static void ReferenceSkeleton(AssetPackage p, AssetBinaryWriter w, IReadOnlyList<AssetBone> bones)
    {
        w.Write(bones.Count);
        foreach (var bone in bones)
        {
            w.Write(p.NameOf(bone.Name)); w.Write(bone.Parent); w.Write(new FString(bone.Name));
        }
        w.Write(bones.Count);
        foreach (var bone in bones)
        {
            var t = bone.ReferencePose;
            var q = Quaternion.Normalize(t.Rotation);
            foreach (double v in new[] { q.X, q.Y, q.Z, q.W, t.Translation.X, t.Translation.Y, t.Translation.Z, t.Scale.X, t.Scale.Y, t.Scale.Z })
                w.Write(v);
        }
        w.Write(bones.Count);
        for (var i = 0; i < bones.Count; i++) { w.Write(p.NameOf(bones[i].Name)); w.Write(i); }
    }
}
