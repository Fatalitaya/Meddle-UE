namespace Meddle.Utils.Audit;

/// <summary>
/// Constants for the temporary runtime audit dump format.
///
/// This is an investigation instrument, NOT a final snapshot ABI. The layout may
/// change freely while the audit branch is used for runtime research.
/// </summary>
public static class RuntimeAuditFormat
{
    /// <summary>
    /// Marker written into every audit file so it can never be mistaken for a
    /// final export snapshot.
    /// </summary>
    public const string FormatMarker = "Experimental audit format";

    public const int SchemaVersion = 1;

    public const string RuntimeJson = "runtime.json";
    public const string ResourcesJson = "resources.json";
    public const string MaterialsJson = "materials.json";
    public const string SkeletonsJson = "skeletons.json";
    public const string DeformersJson = "deformers.json";

    /// <summary>
    /// Files written by <see cref="RuntimeAuditSerializer"/>.
    /// runtime.json is canonical; the remaining files are derived projections.
    /// </summary>
    public static readonly string[] DumpFiles =
    [
        RuntimeJson,
        ResourcesJson,
        MaterialsJson,
        SkeletonsJson,
        DeformersJson
    ];
}
