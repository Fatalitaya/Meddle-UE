namespace Meddle.Utils.Export;

public enum TerritoryGroupMode { All, RepeatedResource, Layer, Instance }

/// <summary>Resource identity includes appearance, but excludes placement: every occurrence is retained.</summary>
public sealed record TerritoryGroupItem<T>(T Item, string ModelPath, string MaterialPath, int MaterialIndex,
    uint StainId, string Layer, System.Numerics.Vector4? LinearStain = null, System.Numerics.Vector4? LayoutColor = null, float? EmissivePower = null);
public sealed record TerritoryExportGroup<T>(string Name, T[] Items);

public static class TerritoryGrouping
{
    public static TerritoryExportGroup<T>[] Build<T>(IEnumerable<TerritoryGroupItem<T>> source, TerritoryGroupMode mode)
    {
        var items = source.ToArray();
        IEnumerable<(string Label, TerritoryGroupItem<T>[] Items)> groups = mode switch
        {
            TerritoryGroupMode.All => items.Length == 0 ? [] : [("All", items)],
            TerritoryGroupMode.RepeatedResource => items.GroupBy(i =>
                    (Model: Normalize(i.ModelPath), Material: Normalize(i.MaterialPath), i.MaterialIndex, i.StainId, i.LinearStain, i.LayoutColor, i.EmissivePower))
                .Select(g => (Path.GetFileNameWithoutExtension(g.Key.Model), g.ToArray())),
            TerritoryGroupMode.Layer => items.GroupBy(i => i.Layer, StringComparer.Ordinal)
                .Select(g => (g.Key, g.ToArray())),
            TerritoryGroupMode.Instance => items.Select(i => (Path.GetFileNameWithoutExtension(i.ModelPath), new[] { i })),
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
        return groups.Select((g, index) => new TerritoryExportGroup<T>(
            $"{index:D5}_{SafeLabel(g.Label)}", g.Items.Select(i => i.Item).ToArray())).ToArray();
    }

    private static string Normalize(string path) => path.Replace('\\', '/').ToLowerInvariant();
    private static string SafeLabel(string label)
    {
        var value = new string(label.Select(c => char.IsLetterOrDigit(c) || c is '_' or '-' ? c : '_').ToArray());
        return value.Length > 100 ? value[^100..] : value;
    }
}
