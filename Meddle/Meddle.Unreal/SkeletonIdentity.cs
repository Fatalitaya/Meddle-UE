using System.Security.Cryptography;
using System.Text;
using System.Numerics;

namespace Meddle.Unreal;

public static class SkeletonIdentity
{
    public static string Hash(IReadOnlyList<AssetBone> bones)
    {
        AssetValidation.Bones(bones);
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
        foreach (var bone in bones)
        {
            writer.Write(bone.Name.ToLowerInvariant()); writer.Write(bone.Parent);
            var t = bone.ReferencePose;
            var q = Quaternion.Normalize(t.Rotation);
            if (q.W < 0) q = -q;
            foreach (var value in new[] { t.Translation.X,t.Translation.Y,t.Translation.Z,q.X,q.Y,q.Z,q.W,t.Scale.X,t.Scale.Y,t.Scale.Z })
                writer.Write((long)Math.Round(value * 100000));
        }
        return Convert.ToHexStringLower(SHA256.HashData(stream.ToArray()));
    }

    public static (string Package, Guid Guid) Write(string destination, IReadOnlyList<AssetBone> bones, string owner)
    {
        if (!AssetNames.IsValid(owner))
            throw new ArgumentException("Use a sanitized character or clip-group name.", nameof(owner));
        var hash = Hash(bones);
        var asset = "SKEL_" + hash;
        // A matching signature deduplicates skins within this export, never assumes
        // that different XIV instances provide a common, interchangeable skeleton.
        var package = "/Game/Meddle/" + owner + "/Skeletons/" + asset;
        var guid = new Guid(Convert.FromHexString(hash[..32]));
        SkeletonWriter.Write(Path.Combine(destination, "Content", "Meddle", owner, "Skeletons", asset + ".uasset"), package, bones, guid);
        return (package, guid);
    }
}
