using System.Numerics;
using System.Text.Json;
using Meddle.Utils.Export.Ue;

namespace Meddle.Utils.Tests;

[TestClass]
public sealed class UeTransformConverterTests
{
    private const float Epsilon = 1e-4f;

    [TestMethod]
    public void NormalTransform_MapsScaleRotationTranslationToCorrectSlots_WithoutSanitization()
    {
        var scale = new Vector3(2, 3, 4);
        var rotation = Quaternion.CreateFromYawPitchRoll(0.3f, -0.2f, 0.5f);
        var translation = new Vector3(11, 22, 33);
        var issues = new List<TransformSanitizeIssue>();

        var snapshot = UeTransformConverter.Convert(
            scale: scale,
            rotation: rotation,
            translation: translation,
            onIssue: issues.Add);

        AssertVectorEqual(translation, snapshot.Translation);
        AssertVectorEqual(scale, snapshot.Scale);
        AssertQuaternionEqual(Quaternion.Normalize(rotation), snapshot.Rotation);
        Assert.AreEqual(0, issues.Count, "a valid transform must not be reported as sanitized");

        // Discriminator for the historical blocker: the old call
        // Sanitize(translation, rotation, scale) swapped the Vector3 slots.
        var buggy = TransformSanitizer.Sanitize(
            scale: translation,
            rotation: rotation,
            translation: scale);
        AssertVector3NotEqual(buggy.Translation, snapshot.Translation);
        AssertVector3NotEqual(buggy.Scale, snapshot.Scale);
    }

    [TestMethod]
    public void NonFiniteScale_SanitizedToOne_WithScaleObservation()
    {
        var issues = new List<TransformSanitizeIssue>();

        var snapshot = UeTransformConverter.Convert(
            scale: new Vector3(float.NaN, 2, 3),
            rotation: Quaternion.Identity,
            translation: Vector3.Zero,
            onIssue: issues.Add);

        AssertVectorEqual(Vector3.One, snapshot.Scale);
        Assert.AreEqual(1, issues.Count);
        Assert.AreEqual(TransformSanitizeComponent.Scale, issues[0].Component);
        Assert.AreEqual("NonFiniteScale", issues[0].Reason);
    }

    [TestMethod]
    public void NearZeroScale_SanitizedToOne_WithScaleObservation()
    {
        var issues = new List<TransformSanitizeIssue>();

        var snapshot = UeTransformConverter.Convert(
            scale: new Vector3(0.5f, 1e-9f, 2f),
            rotation: Quaternion.Identity,
            translation: Vector3.Zero,
            onIssue: issues.Add);

        AssertVectorEqual(Vector3.One, snapshot.Scale);
        Assert.AreEqual(TransformSanitizeComponent.Scale, issues[0].Component);
    }

    [TestMethod]
    public void NonFiniteTranslation_SanitizedToZero_WithTranslationObservation()
    {
        var issues = new List<TransformSanitizeIssue>();

        var snapshot = UeTransformConverter.Convert(
            scale: Vector3.One,
            rotation: Quaternion.Identity,
            translation: new Vector3(float.PositiveInfinity, 0, 0),
            onIssue: issues.Add);

        AssertVectorEqual(Vector3.Zero, snapshot.Translation);
        Assert.AreEqual(TransformSanitizeComponent.Translation, issues[0].Component);
        Assert.AreEqual("NonFiniteTranslation", issues[0].Reason);
    }

    [TestMethod]
    public void NearZeroRotation_SanitizedToIdentity_WithRotationObservation()
    {
        var issues = new List<TransformSanitizeIssue>();

        var snapshot = UeTransformConverter.Convert(
            scale: Vector3.One,
            rotation: new Quaternion(1e-8f, 0, 0, 0),
            translation: Vector3.Zero,
            onIssue: issues.Add);

        AssertQuaternionEqual(Quaternion.Identity, snapshot.Rotation);
        Assert.AreEqual(TransformSanitizeComponent.Rotation, issues[0].Component);
        Assert.AreEqual("NearZeroRotationLength", issues[0].Reason);
    }

    [TestMethod]
    public void NonNormalizedFiniteRotation_IsNormalized_WithRotationObservation()
    {
        var issues = new List<TransformSanitizeIssue>();

        var snapshot = UeTransformConverter.Convert(
            scale: Vector3.One,
            rotation: new Quaternion(2f, 0, 0, 0),
            translation: Vector3.Zero,
            onIssue: issues.Add);

        Assert.AreEqual(1f, new Quaternion(
            snapshot.Rotation[0],
            snapshot.Rotation[1],
            snapshot.Rotation[2],
            snapshot.Rotation[3]).Length(), Epsilon);
        Assert.AreEqual(TransformSanitizeComponent.Rotation, issues[0].Component);
        Assert.AreEqual("NonNormalizedRotation", issues[0].Reason);
    }

    [TestMethod]
    public void ConverterOutput_AllTransformArraysAreFinite()
    {
        var snapshot = new UeCharacterSnapshot
        {
            Root = new UeCharacterNodeSnapshot
            {
                NodeId = "root",
                Skeleton = new UeSkeletonSnapshot
                {
                    Transform = UeTransformConverter.Convert(
                        scale: new Vector3(float.NaN, float.PositiveInfinity, 1e-9f),
                        rotation: new Quaternion(float.NaN, 0, 0, 0),
                        translation: new Vector3(float.NegativeInfinity, float.NaN, 5))
                },
                Attach = new UeAttachSnapshot
                {
                    OffsetTransform = UeTransformConverter.Convert(
                        scale: Vector3.One,
                        rotation: new Quaternion(1e-9f, 0, 0, 0),
                        translation: new Vector3(0, float.PositiveInfinity, 0))
                }
            }
        };

        Assert.IsTrue(UeSnapshotFiniteGuard.AreAllTransformsFinite(snapshot));
    }

    [TestMethod]
    public void Serialization_NeverEmitsNonFiniteLiterals()
    {
        var snapshot = new UeCharacterSnapshot
        {
            Root = new UeCharacterNodeSnapshot
            {
                NodeId = "root",
                Skeleton = new UeSkeletonSnapshot
                {
                    Transform = UeTransformConverter.Convert(
                        scale: new Vector3(float.NaN, 2, 3),
                        rotation: Quaternion.Identity,
                        translation: Vector3.Zero),
                    PartialSkeletons =
                    [
                        new UePartialSkeletonSnapshot
                        {
                            RuntimePoses =
                            [
                                new UeRuntimePoseSnapshot
                                {
                                    Transforms =
                                    [
                                        UeTransformConverter.Convert(
                                            scale: Vector3.One,
                                            rotation: new Quaternion(float.NaN, 0, 0, 0),
                                            translation: new Vector3(0, float.PositiveInfinity, 0))
                                    ]
                                }
                            ]
                        }
                    ]
                },
                Attach = new UeAttachSnapshot
                {
                    OffsetTransform = UeTransformConverter.Convert(
                        scale: Vector3.One,
                        rotation: Quaternion.Identity,
                        translation: new Vector3(0, 0, float.NegativeInfinity)),
                    OwnerSkeleton = new UeSkeletonSnapshot
                    {
                        Transform = UeTransformConverter.Convert(
                            scale: new Vector3(1e-9f, 1, 1),
                            rotation: Quaternion.Identity,
                            translation: Vector3.Zero)
                    }
                },
                Attaches =
                [
                    new UeCharacterNodeSnapshot
                    {
                        NodeId = "root/attach-0",
                        Skeleton = new UeSkeletonSnapshot
                        {
                            Transform = UeTransformConverter.Convert(
                                scale: Vector3.One,
                                rotation: Quaternion.Identity,
                                translation: new Vector3(1, 2, 3))
                        }
                    }
                ]
            }
        };

        var json = UeExportJson.Serialize(snapshot);

        Assert.IsFalse(json.Contains("NaN", StringComparison.Ordinal));
        Assert.IsFalse(json.Contains("Infinity", StringComparison.Ordinal));
        using var document = JsonDocument.Parse(json);
        Assert.AreEqual(JsonValueKind.Object, document.RootElement.ValueKind);
    }

    [TestMethod]
    public void FiniteGuard_DetectsPoisonedTransformRecursively()
    {
        var snapshot = new UeCharacterSnapshot
        {
            Root = new UeCharacterNodeSnapshot
            {
                NodeId = "root",
                Skeleton = new UeSkeletonSnapshot
                {
                    Transform = Poison(translation: float.NaN),
                    PartialSkeletons =
                    [
                        new UePartialSkeletonSnapshot
                        {
                            RuntimePoses =
                            [
                                new UeRuntimePoseSnapshot
                                {
                                    Transforms = [Poison(scale: float.PositiveInfinity)]
                                }
                            ]
                        }
                    ]
                },
                Attach = new UeAttachSnapshot
                {
                    OffsetTransform = Poison(rotation: float.NegativeInfinity),
                    TargetSkeleton = new UeSkeletonSnapshot
                    {
                        Transform = Poison(translation: float.PositiveInfinity)
                    }
                },
                Attaches =
                [
                    new UeCharacterNodeSnapshot
                    {
                        NodeId = "root/attach-0",
                        Skeleton = new UeSkeletonSnapshot
                        {
                            Transform = Poison(scale: float.NaN)
                        }
                    }
                ]
            }
        };

        var issues = UeSnapshotFiniteGuard.FindNonFiniteTransforms(snapshot);

        Assert.AreEqual(5, issues.Count);
        Assert.IsTrue(issues.Any(x => x.Contains("root.skeleton.transform", StringComparison.Ordinal)));
        Assert.IsTrue(issues.Any(x => x.Contains("runtimePoses[0].transforms[0]", StringComparison.Ordinal)));
        Assert.IsTrue(issues.Any(x => x.Contains("root.attach.offsetTransform", StringComparison.Ordinal)));
        Assert.IsTrue(issues.Any(x => x.Contains("root.attach.targetSkeleton.transform", StringComparison.Ordinal)));
        Assert.IsTrue(issues.Any(x => x.Contains("root.attaches[0].skeleton.transform", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Completer_RejectsNonFiniteTransformBeforeSerialization()
    {
        var snapshot = new UeCharacterSnapshot
        {
            Root = new UeCharacterNodeSnapshot
            {
                NodeId = "root",
                Skeleton = new UeSkeletonSnapshot
                {
                    Transform = Poison(translation: float.NaN)
                }
            }
        };
        var bundle = new UeResourceBundle
        {
            Manifest = new UeResourceManifest(),
            DataById = new Dictionary<string, byte[]>()
        };

        var parent = Path.Combine(Path.GetTempPath(), $"meddle-ue-{Guid.NewGuid():N}");
        Directory.CreateDirectory(parent);
        var destination = Path.Combine(parent, "bundle");
        try
        {
            var ex = Assert.ThrowsExactly<UeExportException>(() =>
                UeExportCompleter.Complete(snapshot, bundle, destination));

            StringAssert.Contains(ex.Message, "non-finite");
            Assert.IsFalse(Directory.Exists(destination));
        }
        finally
        {
            if (Directory.Exists(parent))
            {
                Directory.Delete(parent, true);
            }
        }
    }

    private static UeTransformSnapshot Poison(float scale = 0f, float rotation = 0f, float translation = 0f)
    {
        return new UeTransformSnapshot
        {
            Scale = [scale, 1, 1],
            Rotation = [rotation, 0, 0, 1],
            Translation = [translation, 0, 0]
        };
    }

    private static void AssertVector3NotEqual(Vector3 expected, float[] actual)
    {
        var differs = Math.Abs(expected.X - actual[0]) >= Epsilon ||
                      Math.Abs(expected.Y - actual[1]) >= Epsilon ||
                      Math.Abs(expected.Z - actual[2]) >= Epsilon;
        Assert.IsTrue(differs, $"expected {expected} to differ from [{actual[0]}, {actual[1]}, {actual[2]}]");
    }

    private static void AssertVectorEqual(Vector3 expected, float[] actual)
    {
        Assert.AreEqual(expected.X, actual[0], Epsilon, $"X expected {expected.X} actual {actual[0]}");
        Assert.AreEqual(expected.Y, actual[1], Epsilon, $"Y expected {expected.Y} actual {actual[1]}");
        Assert.AreEqual(expected.Z, actual[2], Epsilon, $"Z expected {expected.Z} actual {actual[2]}");
    }

    private static void AssertQuaternionEqual(Quaternion expected, float[] actual)
    {
        Assert.AreEqual(expected.X, actual[0], Epsilon);
        Assert.AreEqual(expected.Y, actual[1], Epsilon);
        Assert.AreEqual(expected.Z, actual[2], Epsilon);
        Assert.AreEqual(expected.W, actual[3], Epsilon);
    }
}
