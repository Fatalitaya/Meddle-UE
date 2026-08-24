using Meddle.Utils.Audit;

namespace Meddle.Utils.Tests;

[TestClass]
public sealed class RecentPapObservationBufferTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime T1 = T0.AddSeconds(1);
    private static readonly DateTime T2 = T0.AddSeconds(2);

    [TestMethod]
    public void Observe_FirstPath_AddsEntry()
    {
        var buffer = new RecentPapObservationBuffer();

        buffer.Observe("chara/human/c0101/anim/a0001.pap", T0, PapResourceObservationSource.Sync);

        var snapshot = buffer.Snapshot();
        Assert.AreEqual(1, snapshot.Count);
        Assert.AreEqual("chara/human/c0101/anim/a0001.pap", snapshot[0].Path);
        Assert.AreEqual(T0, snapshot[0].LastSeenUtc);
        Assert.AreEqual(1, snapshot[0].SeenCount);
        Assert.AreEqual(PapResourceObservationSource.Sync, snapshot[0].Source);
    }

    [TestMethod]
    public void Observe_SamePath_DeduplicatesCaseInsensitive()
    {
        var buffer = new RecentPapObservationBuffer();

        buffer.Observe("chara/human/c0101/anim/a0001.pap", T0, PapResourceObservationSource.Sync);
        buffer.Observe("CHARA/HUMAN/C0101/ANIM/A0001.PAP", T1, PapResourceObservationSource.Async);

        Assert.AreEqual(1, buffer.Count);
        Assert.AreEqual(1, buffer.Snapshot().Count);
    }

    [TestMethod]
    public void Observe_SamePath_UpdatesTimestamp()
    {
        var buffer = new RecentPapObservationBuffer();

        buffer.Observe("chara/a.pap", T0, PapResourceObservationSource.Sync);
        buffer.Observe("chara/a.pap", T1, PapResourceObservationSource.Sync);

        Assert.AreEqual(T1, buffer.Snapshot().Single().LastSeenUtc);
    }

    [TestMethod]
    public void Observe_SamePath_IncrementsSeenCount()
    {
        var buffer = new RecentPapObservationBuffer();

        buffer.Observe("chara/a.pap", T0, PapResourceObservationSource.Sync);
        buffer.Observe("chara/a.pap", T1, PapResourceObservationSource.Sync);
        buffer.Observe("chara/a.pap", T2, PapResourceObservationSource.Sync);

        Assert.AreEqual(3, buffer.Snapshot().Single().SeenCount);
    }

    [TestMethod]
    public void Observe_SamePath_MovesToFront()
    {
        var buffer = new RecentPapObservationBuffer();

        buffer.Observe("chara/a.pap", T0, PapResourceObservationSource.Sync);
        buffer.Observe("chara/b.pap", T1, PapResourceObservationSource.Sync);
        buffer.Observe("chara/a.pap", T2, PapResourceObservationSource.Sync);

        var snapshot = buffer.Snapshot();
        Assert.AreEqual("chara/a.pap", snapshot[0].Path);
        Assert.AreEqual("chara/b.pap", snapshot[1].Path);
    }

    [TestMethod]
    public void Observe_DifferentSources_StoresLatestSource()
    {
        var buffer = new RecentPapObservationBuffer();

        buffer.Observe("chara/a.pap", T0, PapResourceObservationSource.Sync);
        buffer.Observe("chara/a.pap", T1, PapResourceObservationSource.Async);

        Assert.AreEqual(PapResourceObservationSource.Async, buffer.Snapshot().Single().Source);
    }

    [TestMethod]
    public void Capacity_EvictsOldest()
    {
        var buffer = new RecentPapObservationBuffer(capacity: 2);

        buffer.Observe("chara/a.pap", T0, PapResourceObservationSource.Sync);
        buffer.Observe("chara/b.pap", T1, PapResourceObservationSource.Sync);
        buffer.Observe("chara/c.pap", T2, PapResourceObservationSource.Sync);

        Assert.AreEqual(2, buffer.Count);
        var snapshot = buffer.Snapshot();
        Assert.AreEqual("chara/c.pap", snapshot[0].Path);
        Assert.AreEqual("chara/b.pap", snapshot[1].Path);
        Assert.IsFalse(snapshot.Any(x => x.Path == "chara/a.pap"));
    }

    [TestMethod]
    public void Snapshot_NewestFirst()
    {
        var buffer = new RecentPapObservationBuffer();

        buffer.Observe("chara/a.pap", T0, PapResourceObservationSource.Sync);
        buffer.Observe("chara/b.pap", T1, PapResourceObservationSource.Sync);
        buffer.Observe("chara/c.pap", T2, PapResourceObservationSource.Sync);

        var snapshot = buffer.Snapshot();
        CollectionAssert.AreEqual(
            new[] { "chara/c.pap", "chara/b.pap", "chara/a.pap" },
            snapshot.Select(x => x.Path).ToArray());

        // snapshot is detached: mutating the buffer does not change it
        buffer.Observe("chara/d.pap", T2, PapResourceObservationSource.Sync);
        Assert.AreEqual(3, snapshot.Count);
    }

    [TestMethod]
    public void Clear_RemovesAll()
    {
        var buffer = new RecentPapObservationBuffer();

        buffer.Observe("chara/a.pap", T0, PapResourceObservationSource.Sync);
        buffer.Clear();

        Assert.AreEqual(0, buffer.Count);
        Assert.AreEqual(0, buffer.Snapshot().Count);
    }

    [TestMethod]
    public void Observe_RejectsNonUtcTimestamp()
    {
        var buffer = new RecentPapObservationBuffer();

        Assert.ThrowsExactly<ArgumentException>(() =>
            buffer.Observe("chara/a.pap", DateTime.Now, PapResourceObservationSource.Sync));
    }

    [TestMethod]
    public void ConcurrentObserve_DoesNotCorruptBuffer()
    {
        var buffer = new RecentPapObservationBuffer(capacity: 50);
        var paths = Enumerable.Range(0, 80)
                              .Select(i => $"chara/anim/p{i % 40:D2}.pap")
                              .ToArray();

        Parallel.For(0, 200, iteration =>
        {
            var path = paths[iteration % paths.Length];
            buffer.Observe(
                path,
                T0.AddMilliseconds(iteration),
                iteration % 2 == 0 ? PapResourceObservationSource.Sync : PapResourceObservationSource.Async);
        });

        var snapshot = buffer.Snapshot();
        Assert.IsTrue(snapshot.Count <= 50);
        Assert.AreEqual(
            snapshot.Count,
            snapshot.Select(x => x.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            "no duplicate paths (case-insensitive)");
        Assert.IsTrue(snapshot.All(x => x.SeenCount > 0));
        Assert.IsTrue(snapshot.All(x => x.LastSeenUtc.Kind == DateTimeKind.Utc));
    }
}
