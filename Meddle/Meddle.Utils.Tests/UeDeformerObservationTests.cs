using Meddle.Utils.Export.Ue;

namespace Meddle.Utils.Tests;

[TestClass]
public sealed class UeDeformerObservationTests
{
    [TestMethod]
    public void Cache_MatchingIdentity_ReturnsObservation()
    {
        var cache = new DeformerObservationCache();
        var observation = new DeformerObservation
        {
            State = DeformerObservationState.Observed,
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
                State = DeformerObservationState.Observed,
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
                State = DeformerObservationState.Observed
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
                State = DeformerObservationState.ObservedNone
            });

        var actual = cache.TryGet(
            (nint)0x100,
            2,
            (nint)0x500,
            (nint)0x600,
            "chara/human/c0701/obj/body/b0001.mdl");

        Assert.IsNotNull(actual);
        Assert.AreEqual(DeformerObservationState.ObservedNone, actual!.State);
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
                State = DeformerObservationState.Observed
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
                State = DeformerObservationState.ObservedNone
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
            State = DeformerObservationState.ObservedNone
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
