using System.Text.RegularExpressions;

namespace Meddle.Utils.Export.Ue;

public sealed record UeBuildProvenance(
    string? Version,
    string? Commit,
    bool? Dirty,
    string BuildId)
{
    private static readonly Regex CommitPattern = new("^[0-9a-fA-F]{40,64}$", RegexOptions.Compiled);

    public static UeBuildProvenance FromMetadata(
        string? version,
        string? commit,
        string? dirtyValue,
        string buildId)
    {
        return new UeBuildProvenance(
            Normalize(version),
            NormalizeCommit(commit),
            ParseDirty(dirtyValue),
            buildId);
    }

    public static string? NormalizeCommit(string? value)
    {
        var trimmed = Normalize(value);
        return trimmed != null && CommitPattern.IsMatch(trimmed) ? trimmed : null;
    }

    public static bool? ParseDirty(string? value)
    {
        if (bool.TryParse(Normalize(value), out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static string? Normalize(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
