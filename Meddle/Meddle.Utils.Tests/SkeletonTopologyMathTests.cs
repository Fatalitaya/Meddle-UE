using System.Numerics;
using SharpGLTF.Scenes;

namespace Meddle.Utils.Tests;

/// <summary>
/// Phase C regressions for the owner-skeleton RootWorld / BoneToRoot contract.
/// These tests exercise the exact functions production calls:
/// <c>CharacterComposer</c> composes child worlds through
/// <see cref="AttachmentTransformPolicy.ComposeAttachedRootWorld"/> and
/// <c>SkeletonUtils</c> delegates to
/// <see cref="SkeletonTopologyMath.ComputeBoneToRoot"/> /
/// <see cref="SkeletonTopologyMath.ComputeRootWorld"/>.
///
/// Contract (System.Numerics row-vector convention, points are <c>p * M</c>):
/// <code>
/// RootWorld:
///     actual world transform of the skeleton root node,
///     including that root's own local transform.
/// BoneToRoot:
///     current-pose transform from a descendant bone to the owning
///     skeleton root local space. It excludes the root's own local
///     transform: ComputeBoneToRoot(rootBone) == Identity.
/// AttachedRootWorld:
///     childRootLocal * attachBoneToRoot * ownerRootWorld
/// </code>
/// Every scenario is designed so the old foreign-LocalMatrix fallback / old
/// attach-point-world handoff produces a different (wrong) result.
/// </summary>
[TestClass]
public sealed class SkeletonTopologyMathTests
{
    private const float Epsilon = 1e-3f;

    [TestMethod]
    public void OrdinaryWeapon_UsesPlayerCurrentPose_NotReferencePose()
    {
        var instance = Local(new Vector3(100, 20, 30));
        var playerRootRef = Local(Vector3.Zero);
        var playerRootCurrent = Local(new Vector3(0, 2, 0));
        var playerHandRef = Local(Vector3.Zero);
        var playerHandCurrent = Local(new Vector3(0, 1, 0));
        var weapon = Local(new Vector3(0, 0, 4));

        var layout = new NodeBuilder("layout") { LocalMatrix = instance };
        var pRoot = new BoneNodeBuilder("pRoot") { LocalMatrix = playerRootRef };
        var pHand = new BoneNodeBuilder("pHand") { LocalMatrix = playerHandRef };
        var wRoot = new BoneNodeBuilder("wRoot") { LocalMatrix = weapon };
        pHand.AddNode(wRoot);
        pRoot.AddNode(pHand);
        layout.AddNode(pRoot);

        var ownerBones = new HashSet<BoneNodeBuilder> { pRoot, pHand };
        var pose = new Dictionary<BoneNodeBuilder, Matrix4x4>
        {
            [pRoot] = playerRootCurrent,
            [pHand] = playerHandCurrent
        };

        var playerRootWorld = SkeletonTopologyMath.ComputeRootWorld(
            pRoot, ownerBones, b => pose.TryGetValue(b, out var m) ? m : null, instance);
        var handToRoot = SkeletonTopologyMath.ComputeBoneToRoot(
            pHand, ownerBones, b => pose.TryGetValue(b, out var m) ? m : null);
        var weaponWorld = AttachmentTransformPolicy.ComposeAttachedRootWorld(
            wRoot.LocalMatrix, handToRoot, playerRootWorld);

        // weapon (0,0,4) * hand CURRENT (0,1,0) * playerRootWorld
        // (root CURRENT (0,2,0) * instance (100,20,30)).
        AssertVectorEqual(new Vector3(100, 23, 34), Vector3.Transform(Vector3.Zero, weaponWorld));

        // Discriminator: the reference-pose chain gives a different result, so
        // this test fails if current pose is not used.
        var referenceRootWorld = SkeletonTopologyMath.ComputeRootWorld(
            pRoot, ownerBones, _ => playerRootRef, instance);
        var referenceWorld = AttachmentTransformPolicy.ComposeAttachedRootWorld(
            wRoot.LocalMatrix, playerHandRef, referenceRootWorld);
        AssertVectorEqual(new Vector3(100, 20, 34), Vector3.Transform(Vector3.Zero, referenceWorld));
        Assert.IsFalse(MatrixEquals(weaponWorld, referenceWorld));
    }

    [TestMethod]
    public void MountedPlayer_PlayerRootWorld_ContainsMountCurrentPoseAndInstanceOnce()
    {
        var instance = Local(new Vector3(100, 20, 30));
        var mountRootRef = Local(Vector3.Zero);
        var mountRootCurrent = Local(
            Vector3.One,
            Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 4f),
            Vector3.Zero);
        var mountSeatRef = Local(new Vector3(0, 10, 0));
        var mountSeatCurrent = Local(new Vector3(0, 100, 0));
        var playerRootLocal = Local(Vector3.Zero);

        var layout = new NodeBuilder("layout") { LocalMatrix = instance };
        var mRoot = new BoneNodeBuilder("mRoot") { LocalMatrix = mountRootRef };
        var mSeat = new BoneNodeBuilder("mSeat") { LocalMatrix = mountSeatRef };
        var pRoot = new BoneNodeBuilder("pRoot") { LocalMatrix = playerRootLocal };
        mRoot.AddNode(mSeat);
        mSeat.AddNode(pRoot);
        layout.AddNode(mRoot);

        var mountBones = new HashSet<BoneNodeBuilder> { mRoot, mSeat };
        var mountPose = new Dictionary<BoneNodeBuilder, Matrix4x4>
        {
            [mRoot] = mountRootCurrent,
            [mSeat] = mountSeatCurrent
        };

        var mountRootWorld = SkeletonTopologyMath.ComputeRootWorld(
            mRoot, mountBones, b => mountPose.TryGetValue(b, out var m) ? m : null, instance);
        var seatToMountRoot = SkeletonTopologyMath.ComputeBoneToRoot(
            mSeat, mountBones, b => mountPose.TryGetValue(b, out var m) ? m : null);
        var playerRootWorld = AttachmentTransformPolicy.ComposeAttachedRootWorld(
            pRoot.LocalMatrix, seatToMountRoot, mountRootWorld);

        // seat CURRENT (0,100,0) rotated by mount root CURRENT rotZ45, then
        // instance (100,20,30) — each layer exactly once.
        var expectedTranslation = Vector3.Transform(mountSeatCurrent.Translation, mountRootCurrent) +
                                  instance.Translation;
        AssertVectorEqual(expectedTranslation, Vector3.Transform(Vector3.Zero, playerRootWorld));
        AssertVectorEqual(new Vector3(29.289f, 90.711f, 30), Vector3.Transform(Vector3.Zero, playerRootWorld));

        // Discriminator: reference pose (seat (0,10,0), no mount rotation)
        // yields a different placement.
        var referenceRootWorld = SkeletonTopologyMath.ComputeRootWorld(
            mRoot, mountBones, _ => mountRootRef, instance);
        var referencePlacement = AttachmentTransformPolicy.ComposeAttachedRootWorld(
            pRoot.LocalMatrix, mountSeatRef, referenceRootWorld);
        AssertVectorEqual(new Vector3(100, 30, 30), Vector3.Transform(Vector3.Zero, referencePlacement));
        Assert.IsFalse(MatrixEquals(playerRootWorld, referencePlacement));
    }

    [TestMethod]
    public void MountedPlusWeapon_MountCurrentPoseOnce_InstanceOnce_PlayerPoseOnce_OffsetOnce()
    {
        var instance = Matrix4x4.CreateFromQuaternion(
            Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2f)) *
            Matrix4x4.CreateTranslation(new Vector3(100, 20, 30));
        var mountRootCurrent = Local(
            Vector3.One,
            Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 4f),
            Vector3.Zero);
        var mountSeatCurrent = Local(new Vector3(0, 100, 0));
        var playerRootLocal = Local(new Vector3(0, 3, 0));
        var playerHandPose = Local(new Vector3(0, 1, 0));
        var weapon = Local(new Vector3(0, 0, 4));

        var layout = new NodeBuilder("layout") { LocalMatrix = instance };
        var mRoot = new BoneNodeBuilder("mRoot") { LocalMatrix = Local(Vector3.Zero) };
        var mSeat = new BoneNodeBuilder("mSeat") { LocalMatrix = Local(new Vector3(0, 10, 0)) };
        var pRoot = new BoneNodeBuilder("pRoot") { LocalMatrix = playerRootLocal };
        var pHand = new BoneNodeBuilder("pHand") { LocalMatrix = Local(Vector3.Zero) };
        var wRoot = new BoneNodeBuilder("wRoot") { LocalMatrix = weapon };
        pHand.AddNode(wRoot);
        pRoot.AddNode(pHand);
        mSeat.AddNode(pRoot);
        mRoot.AddNode(mSeat);
        layout.AddNode(mRoot);

        var mountBones = new HashSet<BoneNodeBuilder> { mRoot, mSeat };
        var mountPose = new Dictionary<BoneNodeBuilder, Matrix4x4>
        {
            [mRoot] = mountRootCurrent,
            [mSeat] = mountSeatCurrent
        };
        var playerBones = new HashSet<BoneNodeBuilder> { pRoot, pHand };
        var playerPose = new Dictionary<BoneNodeBuilder, Matrix4x4>
        {
            [pRoot] = playerRootLocal,
            [pHand] = playerHandPose
        };

        var mountRootWorld = SkeletonTopologyMath.ComputeRootWorld(
            mRoot, mountBones, b => mountPose.TryGetValue(b, out var m) ? m : null, instance);
        var seatToMountRoot = SkeletonTopologyMath.ComputeBoneToRoot(
            mSeat, mountBones, b => mountPose.TryGetValue(b, out var m) ? m : null);
        var playerRootWorld = AttachmentTransformPolicy.ComposeAttachedRootWorld(
            pRoot.LocalMatrix, seatToMountRoot, mountRootWorld);
        var handToPlayerRoot = SkeletonTopologyMath.ComputeBoneToRoot(
            pHand, playerBones, b => playerPose.TryGetValue(b, out var m) ? m : null);
        var weaponWorld = AttachmentTransformPolicy.ComposeAttachedRootWorld(
            wRoot.LocalMatrix, handToPlayerRoot, playerRootWorld);

        // weapon (0,0,4) * hand (0,1,0) * playerRootWorld
        // (player root (0,3,0) * seat (0,100,0) * mount root rotZ45
        //  * instance rotZ90 + (100,20,30)).
        var afterPlayerRoot = Vector3.Transform(new Vector3(0, 104, 4), mountRootCurrent);
        var expected = Vector3.Transform(afterPlayerRoot, instance);
        AssertVectorEqual(expected, Vector3.Transform(Vector3.Zero, weaponWorld));
        AssertVectorEqual(new Vector3(26.461f, -53.539f, 34), Vector3.Transform(Vector3.Zero, weaponWorld));

        // The production chain equals the explicit child-to-root product, so
        // every layer is present exactly once.
        var canonical = TransformMath.ComposeWorld(
            weapon, playerHandPose, playerRootLocal, mountSeatCurrent, mountRootCurrent, instance);
        AssertMatrixEqual(canonical, weaponWorld);

        // Discriminators: a second instance application, a doubled attach
        // offset, or the mount REFERENCE pose produce different (wrong) worlds.
        var doubledInstance = TransformMath.ComposeWorld(
            weapon, playerHandPose, playerRootLocal, mountSeatCurrent, mountRootCurrent, instance, instance);
        Assert.IsFalse(MatrixEquals(weaponWorld, doubledInstance));
        var doubledOffset = TransformMath.ComposeWorld(
            weapon, weapon, playerHandPose, playerRootLocal, mountSeatCurrent, mountRootCurrent, instance);
        Assert.IsFalse(MatrixEquals(weaponWorld, doubledOffset));
        var referenceMount = TransformMath.ComposeWorld(
            weapon, playerHandPose, playerRootLocal, Local(new Vector3(0, 10, 0)), Local(Vector3.Zero), instance);
        Assert.IsFalse(MatrixEquals(weaponWorld, referenceMount));
    }

    [TestMethod]
    public void ForeignLocalMatrixPoison_IsNeverConsulted()
    {
        var instance = Local(new Vector3(100, 20, 30));
        var poison = Local(new Vector3(9999, 9999, 9999));
        var playerRootPose = Local(new Vector3(0, 2, 0));
        var playerHandPose = Local(new Vector3(0, 1, 0));
        var weapon = Local(new Vector3(0, 0, 4));

        var layout = new NodeBuilder("layout") { LocalMatrix = instance };
        var mRoot = new BoneNodeBuilder("mRoot") { LocalMatrix = poison };
        var mSeat = new BoneNodeBuilder("mSeat") { LocalMatrix = poison };
        var pRoot = new BoneNodeBuilder("pRoot") { LocalMatrix = poison };
        var pHand = new BoneNodeBuilder("pHand") { LocalMatrix = poison };
        var wRoot = new BoneNodeBuilder("wRoot") { LocalMatrix = weapon };
        pHand.AddNode(wRoot);
        pRoot.AddNode(pHand);
        mSeat.AddNode(pRoot);
        mRoot.AddNode(mSeat);
        layout.AddNode(mRoot);

        var ownerBones = new HashSet<BoneNodeBuilder> { pRoot, pHand };
        var pose = new Dictionary<BoneNodeBuilder, Matrix4x4>
        {
            [pRoot] = playerRootPose,
            [pHand] = playerHandPose
        };
        var resolverCalls = new List<BoneNodeBuilder>();

        // The boundary walk must stop at the player root and never touch the
        // foreign mount nodes.
        var chainNodes = SkeletonTopologyMath.CollectOwnerChainNodes(pHand, ownerBones);
        CollectionAssert.AreEquivalent(new BoneNodeBuilder[] { pHand, pRoot }, chainNodes.ToArray());

        var handToRoot = SkeletonTopologyMath.ComputeBoneToRoot(
            pHand,
            ownerBones,
            b =>
            {
                resolverCalls.Add(b);
                return pose.TryGetValue(b, out var m) ? m : null;
            });
        var playerRootWorld = SkeletonTopologyMath.ComputeRootWorld(
            pRoot, ownerBones, b => pose.TryGetValue(b, out var m) ? m : null, instance);
        var weaponWorld = AttachmentTransformPolicy.ComposeAttachedRootWorld(
            wRoot.LocalMatrix, handToRoot, playerRootWorld);

        // The root's own local is excluded from BoneToRoot, so only the hand is
        // resolved there; the root local is resolved through RootWorld.
        Assert.AreEqual(1, resolverCalls.Count);
        Assert.AreSame(pHand, resolverCalls[0]);
        AssertVectorEqual(new Vector3(100, 23, 34), Vector3.Transform(Vector3.Zero, weaponWorld));

        // Discriminator: the historical scene-parent fallback read the foreign
        // nodes' LocalMatrix (poison here), so this scenario fails it.
        var oldStyleFallback = TransformMath.ComposeWorld(
            wRoot.LocalMatrix, playerHandPose, playerRootPose, mSeat.LocalMatrix, mRoot.LocalMatrix, instance);
        Assert.IsTrue(Vector3.Transform(Vector3.Zero, oldStyleFallback).X > 15000f);
    }

    [TestMethod]
    public void NestedAttachment_ParentRootWorldPropagated_ForeignPoisonIgnored()
    {
        var externalPoison = Local(new Vector3(9999, 9999, 9999));
        var instance = Local(new Vector3(100, 0, 0));
        var aBonePose = Local(new Vector3(0, 1, 0));
        var bLocal = Local(new Vector3(0, 0, 2));
        var bBonePose = Local(new Vector3(0, 2, 0));
        var cLocal = Local(new Vector3(3, 0, 0));

        var external = new NodeBuilder("external") { LocalMatrix = externalPoison };
        var layout = new NodeBuilder("layout") { LocalMatrix = instance };
        external.AddNode(layout);

        var aRoot = new BoneNodeBuilder("aRoot") { LocalMatrix = Local(Vector3.Zero) };
        var aBone = new BoneNodeBuilder("aBone") { LocalMatrix = Local(Vector3.Zero) };
        var bRoot = new BoneNodeBuilder("bRoot") { LocalMatrix = bLocal };
        var bBone = new BoneNodeBuilder("bBone") { LocalMatrix = Local(Vector3.Zero) };
        var cRoot = new BoneNodeBuilder("cRoot") { LocalMatrix = cLocal };
        aRoot.AddNode(aBone);
        bRoot.AddNode(bBone);
        layout.AddNode(aRoot);
        aBone.AddNode(bRoot);
        bBone.AddNode(cRoot);

        var aBones = new HashSet<BoneNodeBuilder> { aRoot, aBone };
        var aPose = new Dictionary<BoneNodeBuilder, Matrix4x4> { [aBone] = aBonePose };
        var bBones = new HashSet<BoneNodeBuilder> { bRoot, bBone };
        var bPose = new Dictionary<BoneNodeBuilder, Matrix4x4>
        {
            [bRoot] = Local(Vector3.Zero),
            [bBone] = bBonePose
        };

        // A composes top-level: A.RootWorld = aRootLocal * instance.
        var aRootWorld = SkeletonTopologyMath.ComputeRootWorld(
            aRoot, aBones, b => aPose.TryGetValue(b, out var m) ? m : null, instance);
        var aBoneToRoot = SkeletonTopologyMath.ComputeBoneToRoot(
            aBone, aBones, b => aPose.TryGetValue(b, out var m) ? m : null);

        // B composes at A's attach point: B.RootWorld = B.Local * aBoneToRoot
        // * A.RootWorld — the actual root world, including B's own local
        // (attach offset (0,0,2)).
        var bRootWorld = AttachmentTransformPolicy.ComposeAttachedRootWorld(
            bRoot.LocalMatrix, aBoneToRoot, aRootWorld);
        AssertVectorEqual(new Vector3(100, 1, 2), Vector3.Transform(Vector3.Zero, bRootWorld));

        // C composes at B's attach point, inheriting B's actual RootWorld —
        // never an attach-point world, so B's root local is retained.
        var bBoneToRoot = SkeletonTopologyMath.ComputeBoneToRoot(
            bBone, bBones, b => bPose.TryGetValue(b, out var m) ? m : null);
        var cRootWorld = AttachmentTransformPolicy.ComposeAttachedRootWorld(
            cRoot.LocalMatrix, bBoneToRoot, bRootWorld);

        AssertVectorEqual(new Vector3(103, 3, 2), Vector3.Transform(Vector3.Zero, cRootWorld));
        Assert.IsTrue(TransformMath.IsFinite(cRootWorld));

        // Discriminator A: the old implementation handed B's attach point world
        // to C, dropping B's root local (offset) -> (103,3,0).
        var bAttachPointWorld = AttachmentTransformPolicy.ComposeAttachedRootWorld(
            Matrix4x4.Identity, aBoneToRoot, aRootWorld);
        var oldImplementation = AttachmentTransformPolicy.ComposeAttachedRootWorld(
            cRoot.LocalMatrix, bBoneToRoot, bAttachPointWorld);
        AssertVectorEqual(new Vector3(103, 3, 0), Vector3.Transform(Vector3.Zero, oldImplementation));
        Assert.IsFalse(MatrixEquals(cRootWorld, oldImplementation));

        // Discriminator B: walking the scene graph up to `external` would
        // absorb the poison; the propagated RootWorld path never touches it.
        Assert.IsTrue(Vector3.Transform(Vector3.Zero, cRootWorld).X < 1000f);
    }

    [TestMethod]
    public void NestedAttachment_InheritsLargeParentOffset_ExactlyOnce()
    {
        var instance = Local(new Vector3(100, 0, 0));
        var aBonePose = Local(new Vector3(0, 1, 0));
        var bOffset = Local(new Vector3(0, 0, 500));
        var bBonePose = Local(new Vector3(0, 2, 0));
        var cLocal = Local(new Vector3(3, 0, 0));

        var aRoot = new BoneNodeBuilder("aRoot") { LocalMatrix = Local(Vector3.Zero) };
        var aBone = new BoneNodeBuilder("aBone") { LocalMatrix = Local(Vector3.Zero) };
        var bRoot = new BoneNodeBuilder("bRoot") { LocalMatrix = bOffset };
        var bBone = new BoneNodeBuilder("bBone") { LocalMatrix = Local(Vector3.Zero) };
        var cRoot = new BoneNodeBuilder("cRoot") { LocalMatrix = cLocal };
        aRoot.AddNode(aBone);
        bRoot.AddNode(bBone);
        aBone.AddNode(bRoot);
        bBone.AddNode(cRoot);

        var aBones = new HashSet<BoneNodeBuilder> { aRoot, aBone };
        var aPose = new Dictionary<BoneNodeBuilder, Matrix4x4> { [aBone] = aBonePose };
        var bBones = new HashSet<BoneNodeBuilder> { bRoot, bBone };
        var bPose = new Dictionary<BoneNodeBuilder, Matrix4x4>
        {
            [bRoot] = Local(Vector3.Zero),
            [bBone] = bBonePose
        };

        var aRootWorld = SkeletonTopologyMath.ComputeRootWorld(
            aRoot, aBones, b => aPose.TryGetValue(b, out var m) ? m : null, instance);
        var aBoneToRoot = SkeletonTopologyMath.ComputeBoneToRoot(
            aBone, aBones, b => aPose.TryGetValue(b, out var m) ? m : null);
        var bRootWorld = AttachmentTransformPolicy.ComposeAttachedRootWorld(
            bRoot.LocalMatrix, aBoneToRoot, aRootWorld);
        var bBoneToRoot = SkeletonTopologyMath.ComputeBoneToRoot(
            bBone, bBones, b => bPose.TryGetValue(b, out var m) ? m : null);
        var cRootWorld = AttachmentTransformPolicy.ComposeAttachedRootWorld(
            cRoot.LocalMatrix, bBoneToRoot, bRootWorld);

        var c = Vector3.Transform(Vector3.Zero, cRootWorld);
        AssertVectorEqual(new Vector3(103, 3, 500), c);
        // The offset appears exactly once: not dropped (Z=0) and not doubled
        // (Z=1000).
        Assert.AreNotEqual(0f, c.Z, Epsilon);
        Assert.AreNotEqual(1000f, c.Z, Epsilon);

        // Discriminator: the old attach-point-world handoff yields Z=0.
        var bAttachPointWorld = AttachmentTransformPolicy.ComposeAttachedRootWorld(
            Matrix4x4.Identity, aBoneToRoot, aRootWorld);
        var oldImplementation = AttachmentTransformPolicy.ComposeAttachedRootWorld(
            cRoot.LocalMatrix, bBoneToRoot, bAttachPointWorld);
        AssertVectorEqual(new Vector3(103, 3, 0), Vector3.Transform(Vector3.Zero, oldImplementation));
        Assert.IsFalse(MatrixEquals(cRootWorld, oldImplementation));
    }

    [TestMethod]
    public void NestedAttachment_NonCommutingTransforms()
    {
        var instance = Local(new Vector3(100, 20, 30));
        var aBone = Local(
            Vector3.One,
            Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 6f),
            new Vector3(0, 1, 0));
        var bLocal = Local(
            Vector3.One,
            Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 3f),
            new Vector3(0, 0, 2));
        var bBone = Local(
            Vector3.One,
            Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -MathF.PI / 9f),
            new Vector3(0, 2, 0));
        var cLocal = Local(new Vector3(3, 0, 0));

        var aRoot = new BoneNodeBuilder("aRoot") { LocalMatrix = Local(Vector3.Zero) };
        var aBoneNode = new BoneNodeBuilder("aBone") { LocalMatrix = Local(Vector3.Zero) };
        var bRoot = new BoneNodeBuilder("bRoot") { LocalMatrix = bLocal };
        var bBoneNode = new BoneNodeBuilder("bBone") { LocalMatrix = Local(Vector3.Zero) };
        var cRoot = new BoneNodeBuilder("cRoot") { LocalMatrix = cLocal };
        aRoot.AddNode(aBoneNode);
        bRoot.AddNode(bBoneNode);
        aBoneNode.AddNode(bRoot);
        bBoneNode.AddNode(cRoot);

        var aBones = new HashSet<BoneNodeBuilder> { aRoot, aBoneNode };
        var aPose = new Dictionary<BoneNodeBuilder, Matrix4x4> { [aBoneNode] = aBone };
        var bBones = new HashSet<BoneNodeBuilder> { bRoot, bBoneNode };
        var bPose = new Dictionary<BoneNodeBuilder, Matrix4x4>
        {
            [bRoot] = Local(Vector3.Zero),
            [bBoneNode] = bBone
        };

        var aRootWorld = SkeletonTopologyMath.ComputeRootWorld(
            aRoot, aBones, b => aPose.TryGetValue(b, out var m) ? m : null, instance);
        var aBoneToRoot = SkeletonTopologyMath.ComputeBoneToRoot(
            aBoneNode, aBones, b => aPose.TryGetValue(b, out var m) ? m : null);
        var bRootWorld = AttachmentTransformPolicy.ComposeAttachedRootWorld(
            bRoot.LocalMatrix, aBoneToRoot, aRootWorld);
        var bBoneToRoot = SkeletonTopologyMath.ComputeBoneToRoot(
            bBoneNode, bBones, b => bPose.TryGetValue(b, out var m) ? m : null);
        var cRootWorld = AttachmentTransformPolicy.ComposeAttachedRootWorld(
            cRoot.LocalMatrix, bBoneToRoot, bRootWorld);

        // Contract: C.Local * B.BoneToRoot * B.RootWorld, with
        // B.RootWorld = B.Local * A.BoneToRoot * A.RootWorld.
        var expected = TransformMath.ComposeWorld(cLocal, bBone, bRootWorld);
        AssertMatrixEqual(expected, cRootWorld);
        AssertVectorEqual(new Vector3(99.026f, 23.819f, 32), Vector3.Transform(Vector3.Zero, cRootWorld));

        // Order discriminators: swapping C-local and B-bone, or dropping B's
        // own local, both change the result.
        var swapped = AttachmentTransformPolicy.ComposeAttachedRootWorld(
            bBone, cLocal, bRootWorld);
        Assert.IsFalse(MatrixEquals(cRootWorld, swapped));
        var missingBLocal = AttachmentTransformPolicy.ComposeAttachedRootWorld(
            cLocal, bBoneToRoot,
            AttachmentTransformPolicy.ComposeAttachedRootWorld(
                Matrix4x4.Identity, aBoneToRoot, aRootWorld));
        Assert.IsFalse(MatrixEquals(cRootWorld, missingBLocal));
    }

    [TestMethod]
    public void MissingAttachPoint_FallbackDocumented_ComposesAtSceneRoot()
    {
        // Mirrors HandleAttach's documented fallback: child at the context root
        // inheriting only the instance/world layer.
        var instance = Local(new Vector3(100, 20, 30));
        var childLocal = Local(new Vector3(0, 0, 4));

        var world = AttachmentTransformPolicy.ComposeAttachedRootWorld(
            childLocal, Matrix4x4.Identity, instance);

        AssertVectorEqual(new Vector3(100, 20, 34), Vector3.Transform(Vector3.Zero, world));
    }

    [TestMethod]
    public void ComputeBoneToRoot_WhenBoneIsRoot_ReturnsIdentity()
    {
        var root = new BoneNodeBuilder("root") { LocalMatrix = Local(new Vector3(100, 0, 0)) };
        var ownerBones = new HashSet<BoneNodeBuilder> { root };
        var pose = new Dictionary<BoneNodeBuilder, Matrix4x4>
        {
            [root] = Local(new Vector3(0, 9, 0))
        };

        var boneToRoot = SkeletonTopologyMath.ComputeBoneToRoot(
            root, ownerBones, b => pose.TryGetValue(b, out var m) ? m : null);

        AssertMatrixEqual(Matrix4x4.Identity, boneToRoot);
    }

    [TestMethod]
    public void ComputeBoneToRoot_ChildExcludesRootLocal()
    {
        var rootLocal = Local(new Vector3(100, 0, 0));
        var childLocal = Local(new Vector3(0, 5, 0));
        var root = new BoneNodeBuilder("root") { LocalMatrix = rootLocal };
        var child = new BoneNodeBuilder("child") { LocalMatrix = childLocal };
        root.AddNode(child);
        var ownerBones = new HashSet<BoneNodeBuilder> { root, child };
        var pose = new Dictionary<BoneNodeBuilder, Matrix4x4>
        {
            [root] = rootLocal,
            [child] = childLocal
        };

        var boneToRoot = SkeletonTopologyMath.ComputeBoneToRoot(
            child, ownerBones, b => pose.TryGetValue(b, out var m) ? m : null);

        // BoneToRoot(child) == childLocal only: root local (100,0,0) excluded.
        AssertVectorEqual(new Vector3(0, 5, 0), Vector3.Transform(Vector3.Zero, boneToRoot));
        Assert.IsFalse(MatrixEquals(boneToRoot, TransformMath.ComposeWorld(childLocal, rootLocal)));

        // RootWorld carries the root local exactly once, then ChildWorld =
        // BoneToRoot * RootWorld recovers the full chain.
        var rootWorld = SkeletonTopologyMath.ComputeRootWorld(
            root, ownerBones, b => pose.TryGetValue(b, out var m) ? m : null, Matrix4x4.Identity);
        AssertVectorEqual(new Vector3(100, 0, 0), Vector3.Transform(Vector3.Zero, rootWorld));
        var childWorld = AttachmentTransformPolicy.ComposeAttachedRootWorld(
            Matrix4x4.Identity, boneToRoot, rootWorld);
        AssertVectorEqual(new Vector3(100, 5, 0), Vector3.Transform(Vector3.Zero, childWorld));
    }

    [TestMethod]
    public void ComputeBoneToRoot_GrandchildComposesOwnedAncestors_ExcludingRoot()
    {
        var rootLocal = Local(new Vector3(100, 0, 0));
        var aLocal = Local(new Vector3(0, 1, 0));
        var bLocal = Local(new Vector3(0, 0, 2));
        var root = new BoneNodeBuilder("root") { LocalMatrix = rootLocal };
        var a = new BoneNodeBuilder("a") { LocalMatrix = aLocal };
        var b = new BoneNodeBuilder("b") { LocalMatrix = bLocal };
        root.AddNode(a);
        a.AddNode(b);
        var ownerBones = new HashSet<BoneNodeBuilder> { root, a, b };
        var pose = new Dictionary<BoneNodeBuilder, Matrix4x4>
        {
            [root] = rootLocal,
            [a] = aLocal,
            [b] = bLocal
        };

        var boneToRoot = SkeletonTopologyMath.ComputeBoneToRoot(
            b, ownerBones, b2 => pose.TryGetValue(b2, out var m) ? m : null);

        // B.Local * A.Local = (0,1,2); root local (100,0,0) excluded.
        AssertVectorEqual(new Vector3(0, 1, 2), Vector3.Transform(Vector3.Zero, boneToRoot));
        Assert.IsFalse(MatrixEquals(boneToRoot, TransformMath.ComposeWorld(bLocal, aLocal, rootLocal)));
    }

    [TestMethod]
    public void ComputeBoneToRoot_ThrowsForForeignBone()
    {
        var pRoot = new BoneNodeBuilder("pRoot");
        var pHand = new BoneNodeBuilder("pHand");
        pRoot.AddNode(pHand);
        var ownerBones = new HashSet<BoneNodeBuilder> { pRoot };

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            SkeletonTopologyMath.ComputeBoneToRoot(pHand, ownerBones, _ => null));
    }

    [TestMethod]
    public void GeneratedBone_UsesLocalMatrix_AndIsNeverResolvedAsPose()
    {
        var root = new BoneNodeBuilder("root") { LocalMatrix = Local(Vector3.Zero) };
        var parent = new BoneNodeBuilder("parent") { LocalMatrix = Local(new Vector3(0, 2, 0)) };
        var generated = new BoneNodeBuilder("generated")
        {
            IsGenerated = true,
            LocalMatrix = Local(new Vector3(0, 5, 0))
        };
        root.AddNode(parent);
        parent.AddNode(generated);
        var ownerBones = new HashSet<BoneNodeBuilder> { root, parent, generated };
        var parentPose = Local(new Vector3(0, 2, 0));
        var resolverCalls = new List<BoneNodeBuilder>();

        var boneToRoot = SkeletonTopologyMath.ComputeBoneToRoot(
            generated,
            ownerBones,
            b =>
            {
                resolverCalls.Add(b);
                return b == parent ? parentPose : null;
            });

        // generatedLocal (0,5,0) * parentPose (0,2,0); the root is excluded.
        AssertVectorEqual(new Vector3(0, 7, 0), Vector3.Transform(Vector3.Zero, boneToRoot));
        Assert.AreEqual(1, resolverCalls.Count);
        Assert.AreSame(parent, resolverCalls[0]);
    }

    [TestMethod]
    public void StopAtBoundary_InstanceLayerIncludedOnce()
    {
        var instance = Local(
            new Vector3(2, 1, 1),
            Quaternion.CreateFromYawPitchRoll(0.2f, -0.1f, 0.3f),
            new Vector3(50, 60, 70));
        var externalParent = Local(new Vector3(9, 8, 7));

        var external = new NodeBuilder("external") { LocalMatrix = externalParent };
        var layout = new NodeBuilder("layout") { LocalMatrix = instance };
        external.AddNode(layout);
        var pRoot = new BoneNodeBuilder("pRoot") { LocalMatrix = Local(Vector3.Zero) };
        layout.AddNode(pRoot);

        var ownerBones = new HashSet<BoneNodeBuilder> { pRoot };
        var rootWorld = SkeletonTopologyMath.ComputeRootWorld(
            pRoot, ownerBones, _ => null, instance);

        AssertMatrixEqual(instance, rootWorld);
        Assert.IsFalse(MatrixEquals(rootWorld, TransformMath.ComposeWorld(instance, externalParent)));
    }

    private static Matrix4x4 Local(Vector3 scale, Quaternion rotation, Vector3 translation) =>
        Matrix4x4.CreateScale(scale) *
        Matrix4x4.CreateFromQuaternion(rotation) *
        Matrix4x4.CreateTranslation(translation);

    private static Matrix4x4 Local(Vector3 translation) => Local(Vector3.One, Quaternion.Identity, translation);

    private static void AssertVectorEqual(Vector3 expected, Vector3 actual)
    {
        Assert.AreEqual(expected.X, actual.X, Epsilon, $"X expected {expected.X} actual {actual.X}");
        Assert.AreEqual(expected.Y, actual.Y, Epsilon, $"Y expected {expected.Y} actual {actual.Y}");
        Assert.AreEqual(expected.Z, actual.Z, Epsilon, $"Z expected {expected.Z} actual {actual.Z}");
    }

    private static bool MatrixEquals(Matrix4x4 a, Matrix4x4 b)
    {
        return TransformMath.IsFinite(a) &&
               TransformMath.IsFinite(b) &&
               Math.Abs(a.M11 - b.M11) < Epsilon &&
               Math.Abs(a.M12 - b.M12) < Epsilon &&
               Math.Abs(a.M13 - b.M13) < Epsilon &&
               Math.Abs(a.M14 - b.M14) < Epsilon &&
               Math.Abs(a.M21 - b.M21) < Epsilon &&
               Math.Abs(a.M22 - b.M22) < Epsilon &&
               Math.Abs(a.M23 - b.M23) < Epsilon &&
               Math.Abs(a.M24 - b.M24) < Epsilon &&
               Math.Abs(a.M31 - b.M31) < Epsilon &&
               Math.Abs(a.M32 - b.M32) < Epsilon &&
               Math.Abs(a.M33 - b.M33) < Epsilon &&
               Math.Abs(a.M34 - b.M34) < Epsilon &&
               Math.Abs(a.M41 - b.M41) < Epsilon &&
               Math.Abs(a.M42 - b.M42) < Epsilon &&
               Math.Abs(a.M43 - b.M43) < Epsilon &&
               Math.Abs(a.M44 - b.M44) < Epsilon;
    }

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
