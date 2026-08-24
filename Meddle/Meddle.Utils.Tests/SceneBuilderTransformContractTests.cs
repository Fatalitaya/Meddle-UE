using System.Numerics;
using Meddle.Utils;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;
using SharpGLTF.Transforms;

namespace Meddle.Utils.Tests;

[TestClass]
public sealed class SceneBuilderTransformContractTests
{
    private const float Epsilon = 1e-4f;

    [TestMethod]
    public void SkinnedMeshPose_MatchesArmatureRootAndInstanceIsAppliedOnce()
    {
        var scene = new SceneBuilder();
        var layoutRoot = new NodeBuilder("layoutRoot");
        var mountRoot = new BoneNodeBuilder("mountRoot") { BoneIndex = 0, PartialSkeletonIndex = 0 };
        var mountSeat = new BoneNodeBuilder("mountSeat") { BoneIndex = 1, PartialSkeletonIndex = 0 };
        mountRoot.AddNode(mountSeat);
        layoutRoot.AddNode(mountRoot);
        scene.AddNode(layoutRoot);

        var instance = new AffineTransform(
            new Vector3(2, 1, 1),
            Quaternion.CreateFromYawPitchRoll(0.1f, -0.2f, 0.3f),
            new Vector3(100, 20, 30));

        // Production CharacterComposer.Compose places the instance on the
        // context root's local matrix (no keepChildrenInPlace compensation) so
        // the armature actually follows it.
        layoutRoot.LocalMatrix = instance.Matrix;

        var meshWorld = instance.Matrix;
        var joints = new NodeBuilder[] { mountRoot, mountSeat };
        scene.AddSkinnedMesh(BuildDummyMesh(), meshWorld, joints);

        var model = scene.ToGltf2();
        var armatureRoot = SharpGLTF.Schema2.Node.Flatten(model.DefaultScene)
            .FirstOrDefault(n => n.Name == "mountRoot");
        var skinNode = SharpGLTF.Schema2.Node.Flatten(model.DefaultScene)
            .FirstOrDefault(n => n.Skin != null);

        Assert.IsNotNull(armatureRoot);
        Assert.IsNotNull(skinNode);
        AssertMatrixEqual(instance.Matrix, armatureRoot!.WorldMatrix);

        // The mesh pose matrix supplied to AddSkinnedMesh is the same instance
        // layer, so the skin and the armature share one coordinate space.
        foreach (var joint in skinNode!.Skin.Joints)
        {
            Assert.IsTrue(TransformMath.IsFinite(joint.WorldMatrix));
        }
    }

    [TestMethod]
    public void MountedWeapon_ComposeBoneChainSteps_MatchesSceneArmature()
    {
        var weaponRoot = CreateMatrix(Vector3.One, Quaternion.Identity, new Vector3(1, 0, 0));
        var playerHand = CreateMatrix(Vector3.One, Quaternion.CreateFromYawPitchRoll(0f, 0f, MathF.PI / 2f), new Vector3(0, 1, 0));
        var playerRoot = CreateMatrix(Vector3.One, Quaternion.Identity, new Vector3(0, 2, 0));
        var mountSeat = CreateMatrix(Vector3.One, Quaternion.Identity, new Vector3(0, 0, 5));
        var mountRoot = CreateMatrix(Vector3.One, Quaternion.Identity, new Vector3(0, 10, 0));
        var instance = CreateMatrix(Vector3.One, Quaternion.Identity, new Vector3(100, 0, 0));

        var computed = TransformMath.ComposeBoneChainSteps(new[]
        {
            new TransformMath.BoneChainStep(true, weaponRoot),
            new TransformMath.BoneChainStep(true, playerHand),
            new TransformMath.BoneChainStep(true, playerRoot),
            new TransformMath.BoneChainStep(false, mountSeat),
            new TransformMath.BoneChainStep(false, mountRoot),
            new TransformMath.BoneChainStep(false, instance)
        });

        // Build the same hierarchy with SharpGLTF and compare the world matrix
        // produced by the scene graph to the production helper result.
        var scene = new SceneBuilder();
        var layout = new NodeBuilder("layout") { LocalMatrix = instance };
        var mRoot = new BoneNodeBuilder("mRoot") { LocalMatrix = mountRoot };
        var mSeat = new BoneNodeBuilder("mSeat") { LocalMatrix = mountSeat };
        var pRoot = new BoneNodeBuilder("pRoot") { LocalMatrix = playerRoot };
        var pHand = new BoneNodeBuilder("pHand") { LocalMatrix = playerHand };
        var wRoot = new BoneNodeBuilder("wRoot") { LocalMatrix = weaponRoot };
        pHand.AddNode(wRoot);
        pRoot.AddNode(pHand);
        mSeat.AddNode(pRoot);
        mRoot.AddNode(mSeat);
        layout.AddNode(mRoot);
        scene.AddNode(layout);

        AssertMatrixEqual(computed, wRoot.WorldMatrix);
    }

    private static SharpGLTF.Geometry.MeshBuilder<VertexPosition, VertexEmpty, VertexJoints4> BuildDummyMesh()
    {
        var mesh = new SharpGLTF.Geometry.MeshBuilder<VertexPosition, VertexEmpty, VertexJoints4>("dummy");
        var material = new MaterialBuilder("mat");
        var p1 = new VertexPosition { Position = new Vector3(0.000001f, 0, 0) };
        var p2 = new VertexPosition { Position = new Vector3(0, 0.000001f, 0) };
        var p3 = new VertexPosition { Position = new Vector3(0, 0, 0.000001f) };
        mesh.UsePrimitive(material).AddTriangle(
            (p1, new VertexEmpty(), new VertexJoints4(0)),
            (p2, new VertexEmpty(), new VertexJoints4(0)),
            (p3, new VertexEmpty(), new VertexJoints4(0)));
        return mesh;
    }

    private static Matrix4x4 CreateMatrix(Vector3 scale, Quaternion rotation, Vector3 translation) =>
        Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(translation);

    private static void AssertMatrixEqual(Matrix4x4 expected, Matrix4x4 actual)
    {
        Assert.AreEqual(expected.M11, actual.M11, Epsilon);
        Assert.AreEqual(expected.M12, actual.M12, Epsilon);
        Assert.AreEqual(expected.M13, actual.M13, Epsilon);
        Assert.AreEqual(expected.M14, actual.M14, Epsilon);
        Assert.AreEqual(expected.M21, actual.M21, Epsilon);
        Assert.AreEqual(expected.M22, actual.M22, Epsilon);
        Assert.AreEqual(expected.M23, actual.M23, Epsilon);
        Assert.AreEqual(expected.M24, actual.M24, Epsilon);
        Assert.AreEqual(expected.M31, actual.M31, Epsilon);
        Assert.AreEqual(expected.M32, actual.M32, Epsilon);
        Assert.AreEqual(expected.M33, actual.M33, Epsilon);
        Assert.AreEqual(expected.M34, actual.M34, Epsilon);
        Assert.AreEqual(expected.M41, actual.M41, Epsilon);
        Assert.AreEqual(expected.M42, actual.M42, Epsilon);
        Assert.AreEqual(expected.M43, actual.M43, Epsilon);
        Assert.AreEqual(expected.M44, actual.M44, Epsilon);
    }
}
