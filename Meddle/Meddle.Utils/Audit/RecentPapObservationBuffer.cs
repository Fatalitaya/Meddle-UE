namespace Meddle.Utils.Audit;

/// <summary>
/// Bounded, thread-safe, pure-managed observation buffer for PAP resource
/// paths seen by the optional GetResource hooks.
///
/// Contract:
/// <list type="bullet">
/// <item>Observing the same path again (case-insensitive) increments
/// <c>SeenCount</c>, refreshes <c>LastSeenUtc</c>, stores the latest source and
/// moves the entry to the front.</item>
/// <item>New paths are inserted at the front; when the buffer exceeds
/// <c>Capacity</c> the oldest entry is evicted.</item>
/// <item><see cref="Snapshot"/> returns newest-first copies; internal mutable
/// nodes are never exposed.</item>
/// <item>All timestamps are UTC.</item>
/// </list>
/// </summary>
public sealed class RecentPapObservationBuffer
{
    public const int DefaultCapacity = 200;

    private readonly int capacity;
    private readonly object gate = new();
    private readonly LinkedList<PapResourceObservation> order = new();
    private readonly Dictionary<string, LinkedListNode<PapResourceObservation>> byPath =
        new(StringComparer.OrdinalIgnoreCase);

    public RecentPapObservationBuffer(int capacity = DefaultCapacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "capacity must be positive");
        }

        this.capacity = capacity;
    }

    public int Count
    {
        get
        {
            lock (gate)
            {
                return order.Count;
            }
        }
    }

    public void Observe(string path, DateTime observedAtUtc, PapResourceObservationSource source)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (observedAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("PAP observation timestamps must be UTC.", nameof(observedAtUtc));
        }

        lock (gate)
        {
            if (byPath.TryGetValue(path, out var existing))
            {
                var updated = existing.Value with
                {
                    LastSeenUtc = observedAtUtc,
                    Source = source,
                    SeenCount = existing.Value.SeenCount + 1
                };
                existing.Value = updated;
                order.Remove(existing);
                order.AddFirst(existing);
                return;
            }

            var observation = new PapResourceObservation(path, observedAtUtc, source, 1);
            var node = order.AddFirst(observation);
            byPath.Add(path, node);

            while (order.Count > capacity)
            {
                var last = order.Last;
                if (last == null)
                {
                    break;
                }

                order.RemoveLast();
                byPath.Remove(last.Value.Path);
            }
        }
    }

    /// <summary>
    /// Returns a newest-first copy of the current observations. The returned
    /// list is detached from the internal buffer.
    /// </summary>
    public IReadOnlyList<PapResourceObservation> Snapshot()
    {
        lock (gate)
        {
            return order.ToArray();
        }
    }

    public void Clear()
    {
        lock (gate)
        {
            order.Clear();
            byPath.Clear();
        }
    }
}
