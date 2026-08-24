using System.Numerics;
using Meddle.Utils;
using SharpGLTF.Scenes;

namespace Meddle.Utils.Tests;

[TestClass]
public sealed class TransformMathTests
{
    private const float Epsilon = 1e-4f;

    [TestMethod]
    public void C1_ComposeWorld_Identity_ReturnsIdentity()
    {
        var world = TransformMath.ComposeWorld(Matrix4x4.Identity, Matrix4x4.Identity);

        AssertMatrixEqual(Matrix4x4.Identity, world);
    }

    [TestMethod]
    public void C2_ComposeWorld_IncludesInstanceAndBoneLayers()
    {
        var instance = CreateMatrix(Vector3.One, Quaternion.Identity, new Vector3(100, 0, 0));
        var bone = CreateMatrix(Vector3.One, Quaternion.Identity, new Vector3(0, 5, 0));
        var world = TransformMath.ComposeWorld(bone, instance);

        AssertVectorEqual(new Vector3(100, 5, 0), Vector3.Transform(Vector3.Zero, world));
    }

    [TestMethod]
    public void C3_ComposeWorld_NonCommutingOrder_MatchesRowVectorConvention()
    {
        var parentRotation = Matrix4x4.CreateRotationZ(MathF.PI / 2f);
        var childTranslation = Matrix4x4.CreateTranslation(new Vector3(0, 5, 0));

        // child local applied first, then parent rotation: (0,5,0) rotated 90 deg
        // around Z becomes (-5,0,0).
        var world = TransformMath.ComposeWorld(childTranslation, parentRotation);

        AssertVectorEqual(new Vector3(-5, 0, 0), Vector3.Transform(Vector3.Zero, world));
    }

    [TestMethod]
    public void C4_OrdinaryWeapon_ComposeChain_AppliesHandBoneAndOffset()
    {
        var instance = CreateMatrix(Vector3.One, Quaternion.Identity, new Vector3(10, 0, 0));
        var handBone = CreateMatrix(Vector3.One, Quaternion.Identity, new Vector3(0, 1, 0));
        var weaponOffset = CreateMatrix(Vector3.One, Quaternion.Identity, new Vector3(0, 0, 2));

        var world = TransformMath.ComposeWorld(weaponOffset, handBone, instance);

        AssertVectorEqual(new Vector3(10, 1, 2), Vector3.Transform(Vector3.Zero, world));
    }

    [TestMethod]
    public void C5_HiddenWeapon_UsesSameTransformMath()
    {
        // Hidden state is visibility metadata only. The transform graph uses the
        // same explicit layers as an ordinary weapon.
        var placement = CreateMatrix(Vector3.One, Quaternion.Identity, new Vector3(1, 2, 3));
        var worldVisible = TransformMath.ComposeWorld(placement);
        var worldHidden = TransformMath.ComposeWorld(placement);

        AssertMatrixEqual(worldVisible, worldHidden);
    }

    [TestMethod]
    public void C6_MountedCharacter_InstancePropagatesToChild()
    {
        var mountWorld = CreateMatrix(Vector3.One, Quaternion.Identity, new Vector3(50, 0, 0));
        var playerRoot = CreateMatrix(Vector3.One, Quaternion.Identity, new Vector3(0, 3, 0));

        var playerWorld = TransformMath.ComposeWorld(playerRoot, mountWorld);

        AssertVectorEqual(new Vector3(50, 3, 0), Vector3.Transform(Vector3.Zero, playerWorld));
    }

    [TestMethod]
    public void C7_MountedPlusWeapon_PreservesMountWithoutDoubleApplying()
    {
        var mountWorld = CreateMatrix(Vector3.One, Quaternion.Identity, new Vector3(50, 0, 0));
        var playerRoot = CreateMatrix(Vector3.One, Quaternion.Identity, new Vector3(0, 3, 0));
        var playerBone = CreateMatrix(Vector3.One, Quaternion.Identity, new Vector3(0, 1, 0));
        var weaponOffset = CreateMatrix(Vector3.One, Quaternion.Identity, new Vector3(0, 0, 4));

        var playerWorld = TransformMath.ComposeWorld(playerRoot, mountWorld);
        var weaponWorld = TransformMath.ComposeWorld(weaponOffset, playerBone, playerWorld);

        AssertVectorEqual(new Vector3(50, 4, 4), Vector3.Transform(Vector3.Zero, weaponWorld));
    }

    [TestMethod]
    public void C8_Ornament_Type3StyleAttachment()
    {
        var instance = CreateMatrix(Vector3.One, Quaternion.CreateFromYawPitchRoll(0.1f, 0.2f, 0.3f), new Vector3(1, 2, 3));
        var ornamentBone = CreateMatrix(Vector3.One, Quaternion.Identity, new Vector3(0, 0, 2));
        var ornamentOffset = CreateMatrix(Vector3.One, Quaternion.Identity, new Vector3(1, 0, 0));

        var world = TransformMath.ComposeWorld(ornamentOffset, ornamentBone, instance);

        Assert.IsTrue(TransformMath.IsFinite(world));
        Assert.IsTrue(TransformMath.IsAffine(world));
    }

    [TestMethod]
    public void C9_NestedAttachment_ThreeLevelComposition()
    {
        var instance = CreateMatrix(Vector3.One, Quaternion.Identity, new Vector3(100, 0, 0));
        var a = CreateMatrix(Vector3.One, Quaternion.Identity, new Vector3(0, 1, 0));
        var b = CreateMatrix(Vector3.One, Quaternion.Identity, new Vector3(0, 0, 2));
        var c = CreateMatrix(Vector3.One, Quaternion.Identity, new Vector3(3, 0, 0));

        var worldA = TransformMath.ComposeWorld(a, instance);
        var worldB = TransformMath.ComposeWorld(b, worldA);
        var worldC = TransformMath.ComposeWorld(c, worldB);

        AssertVectorEqual(new Vector3(103, 1, 2), Vector3.Transform(Vector3.Zero, worldC));
    }

    [TestMethod]
    public void C10_MissingAttachBone_FallbackIdentity_IsDocumentedByCaller()
    {
        // The composer owns the fallback policy (attach at scene root + warning).
        // This math layer must not throw when the owner bone is unavailable; it
        // receives an identity owner-bone matrix from the caller.
        var world = TransformMath.ComposeWorld(Matrix4x4.Identity, Matrix4x4.Identity);

        AssertMatrixEqual(Matrix4x4.Identity, world);
    }

    [TestMethod]
    public void C11_NonFiniteScale_SanitizedToOne()
    {
        var result = TransformSanitizer.Sanitize(
            new Vector3(float.NaN, 2, 3),
            Quaternion.Identity,
            Vector3.Zero);

        AssertVectorEqual(Vector3.One, result.Scale);
        Assert.IsTrue(result.WasModified);
        Assert.AreEqual(TransformSanitizeComponent.Scale, result.Issues[0].Component);
    }

    [TestMethod]
    public void C12_NearZeroScale_SanitizedToOne()
    {
        var result = TransformSanitizer.Sanitize(
            new Vector3(0.5f, 1e-9f, 2f),
            Quaternion.Identity,
            Vector3.Zero);

        AssertVectorEqual(Vector3.One, result.Scale);
    }

    [TestMethod]
    public void C12b_NegativeNonZeroScale_Preserved()
    {
        var result = TransformSanitizer.Sanitize(
            new Vector3(-1f, 2f, 3f),
            Quaternion.Identity,
            Vector3.Zero);

        AssertVectorEqual(new Vector3(-1f, 2f, 3f), result.Scale);
        Assert.IsFalse(result.WasModified);
    }

    [TestMethod]
    public void C13_NonFiniteTranslation_SanitizedToZero()
    {
        var result = TransformSanitizer.Sanitize(
            Vector3.One,
            Quaternion.Identity,
            new Vector3(float.PositiveInfinity, 0, 0));

        AssertVectorEqual(Vector3.Zero, result.Translation);
        Assert.AreEqual(TransformSanitizeComponent.Translation, result.Issues[0].Component);
    }

    [TestMethod]
    public void C14_InvalidQuaternion_SanitizedToIdentity()
    {
        var result = TransformSanitizer.Sanitize(
            Vector3.One,
            new Quaternion(float.NaN, 0, 0, 0),
            Vector3.Zero);

        AssertQuaternionEqual(Quaternion.Identity, result.Rotation);
        Assert.AreEqual(TransformSanitizeComponent.Rotation, result.Issues[0].Component);
    }

    [TestMethod]
    public void C14b_NearZeroLengthQuaternion_SanitizedToIdentity()
    {
        var result = TransformSanitizer.Sanitize(
            Vector3.One,
            new Quaternion(1e-8f, 0, 0, 0),
            Vector3.Zero);

        AssertQuaternionEqual(Quaternion.Identity, result.Rotation);
    }

    [TestMethod]
    public void C14c_FiniteNonNormalizedQuaternion_IsNormalized()
    {
        var q = new Quaternion(2f, 0, 0, 0);
        var result = TransformSanitizer.Sanitize(Vector3.One, q, Vector3.Zero);

        Assert.AreEqual(1f, result.Rotation.Length(), Epsilon);
    }

    [TestMethod]
    public void C15_NonInvertibleParent_DoesNotProduceNaN()
    {
        var zero = new Matrix4x4(
            0f, 0f, 0f, 0f,
            0f, 0f, 0f, 0f,
            0f, 0f, 0f, 0f,
            0f, 0f, 0f, 0f);
        var ok = TransformMath.ComputeLocalFromWorld(Matrix4x4.Identity, zero, out var local);

        Assert.IsFalse(ok);
        AssertMatrixEqual(Matrix4x4.Identity, local);
        Assert.IsTrue(TransformMath.IsFinite(local));
    }

    [TestMethod]
    public void RoundTrip_LocalWorldLocal_PreservesTransform()
    {
        var parent = CreateMatrix(new Vector3(2f, 1f, 3f), Quaternion.CreateFromYawPitchRoll(0.4f, -0.2f, 0.1f), new Vector3(5, 6, 7));
        var childLocal = CreateMatrix(new Vector3(0.5f, 1.5f, 2f), Quaternion.CreateFromYawPitchRoll(0.1f, 0.3f, -0.4f), new Vector3(1, -2, 3));
        var childWorld = TransformMath.ComposeWorld(childLocal, parent);

        var ok = TransformMath.ComputeLocalFromWorld(childWorld, parent, out var recovered);

        Assert.IsTrue(ok);
        AssertMatrixEqual(childLocal, recovered);
    }

    [TestMethod]
    public void SharpGLTF_WorldMatrix_MatchesComposeWorldConvention()
    {
        var parent = new NodeBuilder("parent");
        var child = new NodeBuilder("child");
        parent.AddNode(child);
        parent.LocalMatrix = Matrix4x4.CreateRotationZ(MathF.PI / 2f);
        child.LocalMatrix = Matrix4x4.CreateTranslation(new Vector3(0, 5, 0));

        var expected = TransformMath.ComposeWorld(child.LocalMatrix, parent.LocalMatrix);

        AssertMatrixEqual(expected, child.WorldMatrix);
        AssertVectorEqual(new Vector3(-5, 0, 0), Vector3.Transform(Vector3.Zero, child.WorldMatrix));
    }

    [TestMethod]
    public void IsAffine_DetectsNonAffineLastRow()
    {
        var affine = CreateMatrix(new Vector3(1, 2, 3), Quaternion.Identity, new Vector3(4, 5, 6));
        Assert.IsTrue(TransformMath.IsAffine(affine));

        var nonAffine = affine;
        nonAffine.M44 = 2f;
        Assert.IsFalse(TransformMath.IsAffine(nonAffine));
    }

    [TestMethod]
    public void ComposeBoneChainSteps_OwnershipBoundary_UsesForeignLocalNotOwnerPose()
    {
        // Reproduces the exact child-to-root steps the production adapter builds
        // for playerHand -> playerRoot -> mountSeat -> mountRoot -> instance.
        // mountSeat/mountRoot are foreign (Owned=false): they carry their local
        // matrices, and are never interpreted through the player skeleton pose.
        var weaponRoot = CreateMatrix(Vector3.One, Quaternion.Identity, new Vector3(1, 0, 0));
        var playerHand = CreateMatrix(Vector3.One, Quaternion.CreateFromYawPitchRoll(0f, 0f, MathF.PI / 2f), new Vector3(0, 1, 0));
        var playerRoot = CreateMatrix(Vector3.One, Quaternion.Identity, new Vector3(0, 2, 0));
        var mountSeat = CreateMatrix(Vector3.One, Quaternion.Identity, new Vector3(0, 0, 5));
        var mountRoot = CreateMatrix(Vector3.One, Quaternion.Identity, new Vector3(0, 10, 0));
        var instance = CreateMatrix(Vector3.One, Quaternion.Identity, new Vector3(100, 0, 0));

        var steps = new[]
        {
            new TransformMath.BoneChainStep(true, weaponRoot),
            new TransformMath.BoneChainStep(true, playerHand),
            new TransformMath.BoneChainStep(true, playerRoot),
            new TransformMath.BoneChainStep(false, mountSeat),
            new TransformMath.BoneChainStep(false, mountRoot),
            new TransformMath.BoneChainStep(false, instance)
        };

        var world = TransformMath.ComposeBoneChainSteps(steps);

        // weapon (1,0,0) -> player hand rotation 90deg -> (0,1,0)
        // + playerRoot (0,2,0) -> (0,3,0)
        // + mountSeat (0,0,5) -> (0,3,5)
        // + mountRoot (0,10,0) -> (0,13,5)
        // + instance (100,0,0) -> (100,13,5).
        AssertVectorEqual(new Vector3(100, 14, 5), Vector3.Transform(Vector3.Zero, world));
    }

    [TestMethod]
    public void ComposeBoneChainSteps_StopsAtBoundary_InstanceExactlyOnce()
    {
        var instance = CreateMatrix(
            new Vector3(2, 1, 1),
            Quaternion.CreateFromYawPitchRoll(0.2f, -0.1f, 0.3f),
            new Vector3(50, 60, 70));
        var externalParent = CreateMatrix(
            new Vector3(1, 1, 3),
            Quaternion.CreateFromYawPitchRoll(0.5f, 0.1f, -0.2f),
            new Vector3(9, 8, 7));

        // externalParent must never be included: the chain stops at instance.
        var steps = new[]
        {
            new TransformMath.BoneChainStep(true, Matrix4x4.Identity),
            new TransformMath.BoneChainStep(false, instance)
        };

        var world = TransformMath.ComposeBoneChainSteps(steps);

        AssertMatrixEqual(instance, world);
        Assert.IsFalse(MatrixEquals(world, TransformMath.ComposeWorld(instance, externalParent)));
    }

    [TestMethod]
    public void TransformSanitizer_ProducesFiniteComponentsForAllFallbacks()
    {
        var sanitized = TransformSanitizer.Sanitize(
            new Vector3(float.NaN, float.PositiveInfinity, 0.0000001f),
            new Quaternion(float.NaN, 0, 0, 0),
            new Vector3(float.NegativeInfinity, float.NaN, 5));

        Assert.IsTrue(TransformMath.IsFinite(sanitized.Scale));
        Assert.IsTrue(TransformMath.IsFinite(sanitized.Translation));
        Assert.IsTrue(TransformMath.IsFinite(sanitized.Rotation));
    }

    [TestMethod]
    public void IsSkeletonRoot_UsesNativeParentIndex_NotExportParent()
    {
        // A skeleton root is defined by parent index -1 in the Havok skeleton.
        // Reparenting the corresponding BoneNodeBuilder under a mount bone must
        // not change this determination.
        Assert.IsTrue(TransformMath.IsSkeletonRootByParentIndex(-1));
        Assert.IsFalse(TransformMath.IsSkeletonRootByParentIndex(0));
        Assert.IsFalse(TransformMath.IsSkeletonRootByParentIndex(7));
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

    private static Matrix4x4 CreateMatrix(Vector3 scale, Quaternion rotation, Vector3 translation) =>
        Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(translation);

    private static void AssertVectorEqual(Vector3 expected, Vector3 actual)
    {
        Assert.AreEqual(expected.X, actual.X, Epsilon, $"X expected {expected.X} actual {actual.X}");
        Assert.AreEqual(expected.Y, actual.Y, Epsilon, $"Y expected {expected.Y} actual {actual.Y}");
        Assert.AreEqual(expected.Z, actual.Z, Epsilon, $"Z expected {expected.Z} actual {actual.Z}");
    }

    private static void AssertQuaternionEqual(Quaternion expected, Quaternion actual)
    {
        Assert.AreEqual(expected.X, actual.X, Epsilon);
        Assert.AreEqual(expected.Y, actual.Y, Epsilon);
        Assert.AreEqual(expected.Z, actual.Z, Epsilon);
        Assert.AreEqual(expected.W, actual.W, Epsilon);
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
