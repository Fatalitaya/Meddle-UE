using System.Text.Json;

namespace Meddle.Utils.Audit;

public enum RuntimeAuditDiffChange
{
    Added,
    Changed,
    Removed
}

public sealed record RuntimeAuditDiffEntry(
    string Category,
    string SourceType,
    string Identity,
    RuntimeAuditDiffChange Change,
    object? BeforeValue,
    object? AfterValue,
    string? Reason = null);

public sealed class RuntimeAuditDiffResult
{
    public List<RuntimeAuditDiffEntry> Added { get; } = [];
    public List<RuntimeAuditDiffEntry> Changed { get; } = [];
    public List<RuntimeAuditDiffEntry> Removed { get; } = [];

    public bool IsEmpty => Added.Count == 0 && Changed.Count == 0 && Removed.Count == 0;

    public Dictionary<string, int> Summarize()
    {
        return new Dictionary<string, int>
        {
            ["added"] = Added.Count,
            ["changed"] = Changed.Count,
            ["removed"] = Removed.Count
        };
    }
}

/// <summary>
/// Compares two audit captures. This is the main research tool: capture A,
/// change the character (equipment, dye, hair, height, face), capture B, then
/// diff A B to see exactly which runtime facts changed.
///
/// Diffing is deterministic: identical inputs always produce identical output
/// ordering. Comparison is semantic (JSON value equality), not textual.
/// </summary>
public static class RuntimeAuditDiff
{
    public static RuntimeAuditDiffResult Diff(
        RuntimeAuditSession before,
        RuntimeAuditSession after)
    {
        var result = new RuntimeAuditDiffResult();
        var beforeEntries = Flatten(before);
        var afterEntries = Flatten(after);

        var beforeKeys = new HashSet<string>(beforeEntries.Keys, StringComparer.Ordinal);
        var afterKeys = new HashSet<string>(afterEntries.Keys, StringComparer.Ordinal);

        foreach (var key in afterKeys.Except(beforeKeys).OrderBy(k => k, StringComparer.Ordinal))
        {
            var entry = afterEntries[key];
            result.Added.Add(entry with
            {
                Change = RuntimeAuditDiffChange.Added,
                BeforeValue = null,
                Reason = "present in after, absent in before"
            });
        }

        foreach (var key in beforeKeys.Except(afterKeys).OrderBy(k => k, StringComparer.Ordinal))
        {
            var entry = beforeEntries[key];
            result.Removed.Add(entry with
            {
                Change = RuntimeAuditDiffChange.Removed,
                AfterValue = null,
                Reason = "present in before, absent in after"
            });
        }

        foreach (var key in beforeKeys.Intersect(afterKeys).OrderBy(k => k, StringComparer.Ordinal))
        {
            var beforeEntry = beforeEntries[key];
            var afterEntry = afterEntries[key];
            if (ValuesEqual(beforeEntry.AfterValue, afterEntry.AfterValue))
            {
                continue;
            }

            result.Changed.Add(new RuntimeAuditDiffEntry(
                afterEntry.Category,
                afterEntry.SourceType,
                afterEntry.Identity,
                RuntimeAuditDiffChange.Changed,
                beforeEntry.AfterValue,
                afterEntry.AfterValue,
                "value changed"));
        }

        return result;
    }

    /// <summary>
    /// Expands a session into stable diff entries.
    ///
    /// Resource observations (models, skeletons, materials, textures) are keyed
    /// by their logical/resolved path when one exists, otherwise by their slot
    /// context. Customize observations are flattened into scalar fields so a
    /// single "height changed" fact shows up as a Changed entry.
    /// </summary>
    private static Dictionary<string, RuntimeAuditDiffEntry> Flatten(RuntimeAuditSession session)
    {
        var entries = new Dictionary<string, RuntimeAuditDiffEntry>(StringComparer.Ordinal);
        foreach (var observation in session.Observations)
        {
            var category = observation.Category;
            var sourceType = observation.SourceType;

            if (string.Equals(category, RuntimeAuditCategory.Customize.ToString(), StringComparison.Ordinal) &&
                observation.Value is Dictionary<string, object?> customizeDict)
            {
                foreach (var (path, value) in FlattenScalars(customizeDict))
                {
                    var key = $"{category}|{sourceType}|customize:{path}";
                    entries[key] = new RuntimeAuditDiffEntry(
                        category,
                        sourceType,
                        path,
                        RuntimeAuditDiffChange.Changed,
                        value,
                        value);
                }

                continue;
            }

            var identity = ResolveIdentity(observation);
            var entryKey = $"{category}|{sourceType}|{identity}";
            entries[entryKey] = new RuntimeAuditDiffEntry(
                category,
                sourceType,
                identity,
                RuntimeAuditDiffChange.Changed,
                observation.Value,
                observation.Value);
        }

        return entries;
    }

    private static string ResolveIdentity(RuntimeAuditObservation observation)
    {
        if (observation.Value is Dictionary<string, object?> dict)
        {
            foreach (var candidate in new[]
                     {
                         "logicalPath",
                         "path",
                         "resource",
                         "resourcePath",
                         "handlePath",
                         "handle",
                         "sourcePath"
                     })
            {
                if (dict.TryGetValue(candidate, out var value) &&
                    value is string text &&
                    !string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }

            var slot = dict.TryGetValue("slotIndex", out var slotValue) ? slotValue?.ToString() : null;
            var index = dict.TryGetValue("index", out var indexValue) ? indexValue?.ToString() : null;
            if (!string.IsNullOrWhiteSpace(slot) || !string.IsNullOrWhiteSpace(index))
            {
                return $"slot:{slot ?? "-"}:index:{index ?? "-"}";
            }
        }

        return observation.ActorId.Length == 0 ? "default" : observation.ActorId;
    }

    private static IEnumerable<KeyValuePair<string, object?>> FlattenScalars(
        Dictionary<string, object?> dict)
    {
        foreach (var (key, value) in dict.OrderBy(kvp => kvp.Key, StringComparer.Ordinal))
        {
            if (value is Dictionary<string, object?> nested)
            {
                foreach (var (nestedKey, nestedValue) in FlattenScalars(nested))
                {
                    yield return new KeyValuePair<string, object?>($"{key}.{nestedKey}", nestedValue);
                }
            }
            else
            {
                yield return new KeyValuePair<string, object?>(key, value);
            }
        }
    }

    private static bool ValuesEqual(object? left, object? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        using var leftDoc = JsonDocument.Parse(JsonSerializer.Serialize(left, RuntimeAuditSerializer.JsonOptions));
        using var rightDoc = JsonDocument.Parse(JsonSerializer.Serialize(right, RuntimeAuditSerializer.JsonOptions));
        return JsonElement.DeepEquals(leftDoc.RootElement, rightDoc.RootElement);
    }
}
