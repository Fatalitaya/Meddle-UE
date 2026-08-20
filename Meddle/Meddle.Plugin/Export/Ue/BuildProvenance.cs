using System.Reflection;
using Meddle.Utils.Export.Ue;

namespace Meddle.Plugin.Export.Ue;

internal static class BuildProvenance
{
    private static readonly Assembly PluginAssembly = typeof(BuildProvenance).Assembly;

    public static UeBuildProvenance Current { get; } = UeBuildProvenance.FromMetadata(
        GetMetadata("MeddleBuildVersion"),
        GetMetadata("MeddleBuildCommit"),
        GetMetadata("MeddleBuildDirty"),
        PluginAssembly.ManifestModule.ModuleVersionId.ToString("N"));

    private static string? GetMetadata(string name)
    {
        var value = PluginAssembly
                    .GetCustomAttributes<AssemblyMetadataAttribute>()
                    .FirstOrDefault(x => string.Equals(x.Key, name, StringComparison.Ordinal))
                    ?.Value;
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
