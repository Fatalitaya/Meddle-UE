using System.Numerics;
using Meddle.Unreal;
using UAssetAPI;
using UAssetAPI.UnrealTypes;

namespace Meddle.Utils.Tests;

[TestClass]
public sealed class NativeAssetTests
{
    private static readonly AssetBone[] Bones = [new("root_0", -1, BoneTransform.Identity),new("child_1",0,new(new(0,0,10),Quaternion.Identity,Vector3.One))];

    [TestMethod]
    public void Coordinates_ConvertAxesAndCentimetres()
    {
        Assert.AreEqual(new Vector3(-300,100,200), Coordinates.Position(new(1,2,3)));
        var transform = Matrix4x4.CreateFromQuaternion(Quaternion.CreateFromAxisAngle(Vector3.UnitY, .7f)) * Matrix4x4.CreateTranslation(1,2,3);
        var converted = Coordinates.Transform(transform);
        var point = new Vector3(2,3,4);
        var expected = Coordinates.Position(Vector3.Transform(point, transform));
        var actual = Vector3.Transform(Coordinates.Position(point), converted.Rotation) + converted.Translation;
        Assert.IsTrue(Vector3.Distance(expected, actual) < .001f);
    }

    [TestMethod]
    public void Identity_TracksTopologyAndReferencePose()
    {
        var hash = SkeletonIdentity.Hash(Bones);
        Assert.AreEqual(hash, SkeletonIdentity.Hash(Bones.ToArray()));
        var changed = Bones.ToArray(); changed[1] = changed[1] with { ReferencePose = BoneTransform.Identity };
        Assert.AreNotEqual(hash, SkeletonIdentity.Hash(changed));
        changed = Bones.ToArray(); changed[1] = changed[1] with { Name = "other" };
        Assert.AreNotEqual(hash, SkeletonIdentity.Hash(changed));
    }

    [TestMethod]
    public void Skeleton_UsesEngineNumberedFNames()
    {
        InTemp(folder =>
        {
            var file = Path.Combine(folder,"SKEL_0.uasset");
            SkeletonWriter.Write(file,"/Game/Test/SKEL_0",Bones);
            var asset = new UAsset(file, EngineVersion.VER_UE5_8);
            Assert.AreEqual("SKEL",asset.Exports[0].ObjectName.Value.Value);
            Assert.AreEqual(1,asset.Exports[0].ObjectName.Number);
            Assert.IsTrue(asset.Imports.Any(i => i.ObjectName.ToString() == "Skeleton"));
        });
    }

    [TestMethod]
    public void Animation_RejectsMismatchedTracksBeforeWriting()
    {
        InTemp(folder =>
        {
            var file = Path.Combine(folder,"Bad.uasset");
            Assert.Throws<ArgumentException>(() => AnimationWriter.Write(file,"/Game/Test/Bad","/Game/Test/Skeleton",Guid.NewGuid(),Bones,
                new(30,30,[new(0,[BoneTransform.Identity])])));
            Assert.IsFalse(File.Exists(file));
        });
    }

    [TestMethod]
    public void Skeleton_RejectsMultipleRootsAndNonfiniteTransforms()
    {
        Assert.Throws<ArgumentException>(() => SkeletonIdentity.Hash([Bones[0],Bones[1] with { Parent = -1 }]));
        Assert.Throws<ArgumentException>(() => SkeletonIdentity.Hash([Bones[0] with { ReferencePose = new(new(float.NaN,0,0),Quaternion.Identity,Vector3.One) }]));
    }

    [TestMethod]
    public void RootMotion_PreservesRotatingPivotAndStartsAtOrigin()
    {
        AssetBone[] bones = [new("Meddle_Root",-1,BoneTransform.Identity),new("root",0,new(new(0,0,10),Quaternion.Identity,Vector3.One))];
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY,.8f);
        BoneTransform[] poses = [new(new(10,0,10),Quaternion.Identity,Vector3.One),new(new(20,0,10),rotation,Vector3.One)];
        var animation = new BakedAnimation(30,1,[new(0,[BoneTransform.Identity,BoneTransform.Identity]),new(1,poses.ToArray())]);
        RootMotion.Extract(bones, animation, false);
        Assert.AreEqual(Vector3.Zero,animation.Tracks[0].Keys[0].Translation);
        var trajectory = animation.Tracks[0].Keys[1]; var local = animation.Tracks[1].Keys[1];
        var world = Vector3.Transform(local.Translation,trajectory.Rotation)+trajectory.Translation;
        Assert.IsTrue(Vector3.Distance(world,new(10,0,10)) < .001);
        Assert.IsTrue(Math.Abs(Quaternion.Dot(local.Rotation*trajectory.Rotation,rotation)) > .99999);
        var inPlace = new BakedAnimation(30,1,[new(0,[BoneTransform.Identity,BoneTransform.Identity]),new(1,poses.ToArray())]);
        RootMotion.Extract(bones,inPlace,true);
        Assert.AreEqual(BoneTransform.Identity,inPlace.Tracks[0].Keys[1]);
        Assert.AreEqual(bones[1].ReferencePose.Translation,inPlace.Tracks[1].Keys[1].Translation);
    }

    [TestMethod]
    public void Texture_RejectsIncorrectPixelLength()
        => Assert.Throws<ArgumentException>(() => TextureWriter.Write("unused.uasset","/Game/Test/Texture",2,2,[0,0,0,0],true));

    private static void InTemp(Action<string> action)
    {
        var directory = Path.Combine(Path.GetTempPath(),"MeddleNativeTests",Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try { action(directory); }
        finally
        {
            var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(),"MeddleNativeTests"))+Path.DirectorySeparatorChar;
            if (Path.GetFullPath(directory).StartsWith(root,StringComparison.OrdinalIgnoreCase)) Directory.Delete(directory,true);
        }
    }
}
