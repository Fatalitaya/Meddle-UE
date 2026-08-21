namespace Meddle.Utils.Export.Ue;

public sealed class DeformerObservationCache
{
    public sealed record DeformerObservationCacheEntry(
        nint HumanPtr,
        uint Slot,
        nint ModelPtr,
        nint ModelResourceHandlePtr,
        string? ModelPath,
        DeformerObservation Observation);

    private sealed record CacheEntry(
        long HumanPtr,
        uint Slot,
        long ModelPtr,
        long ModelResourceHandlePtr,
        string? ModelPath,
        DeformerObservation Observation);

    private readonly Dictionary<(long HumanPtr, uint Slot), CacheEntry> entries = new();
    private readonly object gate = new();

    public void Store(
        nint humanPtr,
        uint slot,
        nint modelPtr,
        nint modelResourceHandlePtr,
        string? modelPath,
        DeformerObservation observation)
    {
        lock (gate)
        {
            // A cache entry is only reusable when every identity field can be
            // compared again later. Observations without a stable model
            // identity fail closed instead of being cached by pointer + slot.
            if (humanPtr == 0 ||
                modelPtr == 0 ||
                modelResourceHandlePtr == 0 ||
                string.IsNullOrWhiteSpace(modelPath))
            {
                return;
            }

            entries[(humanPtr.ToInt64(), slot)] = new CacheEntry(
                humanPtr.ToInt64(),
                slot,
                modelPtr.ToInt64(),
                modelResourceHandlePtr.ToInt64(),
                modelPath,
                observation);
        }
    }

    public DeformerObservation? TryGet(
        nint humanPtr,
        uint slot,
        nint modelPtr,
        nint modelResourceHandlePtr,
        string? modelPath)
    {
        lock (gate)
        {
            var key = (humanPtr.ToInt64(), slot);
            if (!entries.TryGetValue(key, out var entry))
            {
                return null;
            }

            if (entry.ModelPtr != modelPtr.ToInt64() ||
                entry.ModelResourceHandlePtr != modelResourceHandlePtr.ToInt64() ||
                !string.Equals(entry.ModelPath, modelPath, StringComparison.Ordinal))
            {
                entries.Remove(key);
                return null;
            }

            return entry.Observation;
        }
    }

    public IReadOnlyList<DeformerObservationCacheEntry> GetSnapshots()
    {
        lock (gate)
        {
            return entries.Values
                          .Select(x => new DeformerObservationCacheEntry(
                              (nint)x.HumanPtr,
                              x.Slot,
                              (nint)x.ModelPtr,
                              (nint)x.ModelResourceHandlePtr,
                              x.ModelPath,
                              x.Observation))
                          .ToArray();
        }
    }

    public void ClearHuman(nint humanPtr)
    {
        lock (gate)
        {
            var key = humanPtr.ToInt64();
            var remove = entries
                         .Where(x => x.Value.HumanPtr == key)
                         .Select(x => x.Key)
                         .ToArray();

            foreach (var item in remove)
            {
                entries.Remove(item);
            }
        }
    }

    public void PruneToAliveHumans(IReadOnlyCollection<long> aliveHumanPtrs)
    {
        if (aliveHumanPtrs == null)
        {
            return;
        }

        lock (gate)
        {
            var alive = new HashSet<long>(aliveHumanPtrs);
            var remove = entries
                         .Where(x => !alive.Contains(x.Value.HumanPtr))
                         .Select(x => x.Key)
                         .ToArray();

            foreach (var item in remove)
            {
                entries.Remove(item);
            }
        }
    }

    public void Clear()
    {
        lock (gate)
        {
            entries.Clear();
        }
    }
}
