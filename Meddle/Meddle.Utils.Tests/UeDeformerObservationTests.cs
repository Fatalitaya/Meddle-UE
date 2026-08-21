using Meddle.Utils.Export.Ue;

namespace Meddle.Utils.Tests;

[TestClass]
public sealed class UeDeformerObservationTests
{
    [TestMethod]
    public void Observed_UsesRawRuntimeValuesWithoutPathInference()
    {
        var observation = new DeformerObservation
        {
            State = DeformerObservationState.Present,
            RaceSexId = 1401,
            DeformerId = 13,
            PbdPath = "chara/xls/boneDeformer/human.pbd"
        };

        var snapshot = UeDeformerSnapshotFactory.FromObservation(observation, isHuman: true);
        var json = UeExportJson.Serialize(snapshot);

        Assert.AreEqual(UeDeformerStatus.Observed, snapshot.Status);
        Assert.AreEqual(1401, snapshot.RaceSexId);
        Assert.AreEqual(13, snapshot.DeformerId);
        Assert.AreEqual("chara/xls/boneDeformer/human.pbd", snapshot.PbdResourcePath);
        Assert.IsFalse(json.Contains("sourceRace", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(json.Contains("targetRace", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void ObservedNone_IsDistinctFromNotObserved()
    {
        var observation = new DeformerObservation
        {
            State = DeformerObservationState.Absent
        };

        var snapshot = UeDeformerSnapshotFactory.FromObservation(observation, isHuman: true);

        Assert.AreEqual(UeDeformerStatus.ObservedNone, snapshot.Status);
        Assert.IsNull(snapshot.RaceSexId);
        Assert.IsNull(snapshot.DeformerId);
        Assert.IsNull(snapshot.PbdResourcePath);
    }

    [TestMethod]
    public void Incomplete_PreservesObservedScalarsButNoPbdRelationship()
    {
        var observation = new DeformerObservation
        {
            State = DeformerObservationState.Incomplete,
            RaceSexId = 801,
            DeformerId = 7
        };

        var snapshot = UeDeformerSnapshotFactory.FromObservation(observation, isHuman: true);

        Assert.AreEqual(UeDeformerStatus.ObservedIncomplete, snapshot.Status);
        Assert.AreEqual(801, snapshot.RaceSexId);
        Assert.AreEqual(7, snapshot.DeformerId);
        Assert.IsNull(snapshot.PbdResourcePath);
    }

    [TestMethod]
    public void NoObservation_IsNotObserved()
    {
        var snapshot = UeDeformerSnapshotFactory.FromObservation(null, isHuman: true);

        Assert.AreEqual(UeDeformerStatus.NotObserved, snapshot.Status);
        Assert.IsNull(snapshot.PbdResourcePath);
    }

    [TestMethod]
    public void NonHuman_IsNotApplicable()
    {
        var snapshot = UeDeformerSnapshotFactory.FromObservation(null, isHuman: false);

        Assert.AreEqual(UeDeformerStatus.NotApplicable, snapshot.Status);
    }

    [TestMethod]
    public void Cache_MatchingIdentity_ReturnsObservation()
    {
        var cache = new DeformerObservationCache();
        var observation = new DeformerObservation
        {
            State = DeformerObservationState.Present,
            RaceSexId = 801,
            DeformerId = 7,
            PbdPath = "chara/xls/boneDeformer/human.pbd"
        };

        cache.Store(
            (nint)0x100,
            2,
            (nint)0x500,
            (nint)0x600,
            "chara/human/c0701/obj/body/b0001.mdl",
            observation);

        var actual = cache.TryGet(
            (nint)0x100,
            2,
            (nint)0x500,
            (nint)0x600,
            "chara/human/c0701/obj/body/b0001.mdl");

        Assert.AreSame(observation, actual);
    }

    [TestMethod]
    public void Cache_StaleModelIdentity_ReturnsNotObserved()
    {
        var cache = new DeformerObservationCache();
        cache.Store(
            (nint)0x100,
            2,
            (nint)0x500,
            (nint)0x600,
            "chara/human/c0701/obj/body/b0001.mdl",
            new DeformerObservation
            {
                State = DeformerObservationState.Present,
                RaceSexId = 801,
                DeformerId = 7
            });

        var actual = cache.TryGet(
            (nint)0x100,
            2,
            (nint)0x700,
            (nint)0x600,
            "chara/human/c0701/obj/body/b0001.mdl");

        Assert.IsNull(actual);
        Assert.AreEqual(0, cache.GetSnapshots().Count);
    }

    [TestMethod]
    public void Cache_StaleResourceHandle_ReturnsNotObserved()
    {
        var cache = new DeformerObservationCache();
        cache.Store(
            (nint)0x100,
            2,
            (nint)0x500,
            (nint)0x600,
            "chara/human/c0701/obj/body/b0001.mdl",
            new DeformerObservation
            {
                State = DeformerObservationState.Present
            });

        var actual = cache.TryGet(
            (nint)0x100,
            2,
            (nint)0x500,
            (nint)0x700,
            "chara/human/c0701/obj/body/b0001.mdl");

        Assert.IsNull(actual);
        Assert.AreEqual(0, cache.GetSnapshots().Count);
    }

    [TestMethod]
    public void Cache_NoRecord_ReturnsNotObserved()
    {
        var cache = new DeformerObservationCache();

        var actual = cache.TryGet(
            (nint)0x100,
            2,
            (nint)0x500,
            (nint)0x600,
            "chara/human/c0701/obj/body/b0001.mdl");

        Assert.IsNull(actual);
    }

    [TestMethod]
    public void Cache_ObservedAbsent_RemainsAbsentOnMatchingIdentity()
    {
        var cache = new DeformerObservationCache();
        cache.Store(
            (nint)0x100,
            2,
            (nint)0x500,
            (nint)0x600,
            "chara/human/c0701/obj/body/b0001.mdl",
            new DeformerObservation
            {
                State = DeformerObservationState.Absent
            });

        var actual = cache.TryGet(
            (nint)0x100,
            2,
            (nint)0x500,
            (nint)0x600,
            "chara/human/c0701/obj/body/b0001.mdl");

        Assert.IsNotNull(actual);
        Assert.AreEqual(DeformerObservationState.Absent, actual!.State);
    }

    [TestMethod]
    public void Cache_IncompleteModelIdentity_IsNotStored()
    {
        var cache = new DeformerObservationCache();
        cache.Store(
            (nint)0x100,
            2,
            (nint)0x500,
            0,
            null,
            new DeformerObservation
            {
                State = DeformerObservationState.Present
            });

        var actual = cache.TryGet(
            (nint)0x100,
            2,
            (nint)0x500,
            0,
            null);

        Assert.IsNull(actual);
        Assert.AreEqual(0, cache.GetSnapshots().Count);
    }

    [TestMethod]
    public void Cache_PruneRemovesDestroyedHuman()
    {
        var cache = new DeformerObservationCache();
        cache.Store(
            (nint)0x100,
            2,
            (nint)0x500,
            (nint)0x600,
            "chara/human/c0701/obj/body/b0001.mdl",
            new DeformerObservation
            {
                State = DeformerObservationState.Absent
            });

        cache.PruneToAliveHumans([0x200]);

        Assert.IsNull(cache.TryGet(
            (nint)0x100,
            2,
            (nint)0x500,
            (nint)0x600,
            "chara/human/c0701/obj/body/b0001.mdl"));
        Assert.AreEqual(0, cache.GetSnapshots().Count);
    }

    [TestMethod]
    public void Cache_PruneKeepsAliveHuman()
    {
        var cache = new DeformerObservationCache();
        var observation = new DeformerObservation
        {
            State = DeformerObservationState.Absent
        };
        cache.Store(
            (nint)0x100,
            2,
            (nint)0x500,
            (nint)0x600,
            "chara/human/c0701/obj/body/b0001.mdl",
            observation);

        cache.PruneToAliveHumans([0x100]);

        Assert.AreSame(observation, cache.TryGet(
            (nint)0x100,
            2,
            (nint)0x500,
            (nint)0x600,
            "chara/human/c0701/obj/body/b0001.mdl"));
    }
}
