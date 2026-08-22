using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Meddle.Utils.Audit;

/// <summary>
/// Reads and writes the temporary audit dump layout:
///
/// AuditDump/
///     &lt;capture&gt;/
///         runtime.json      (canonical session: metadata + all observations)
///         resources.json    (projection: CharacterComposition + RuntimeTexture)
///         materials.json    (projection: Material)
///         skeletons.json    (projection: SkeletonEvidence)
///         deformers.json    (projection: Deformer)
///         FFXIVCharacterRuntimeAudit.json
///                          (consolidated character audit report)
///
/// Every file is marked "Experimental audit format". Missing files are never
/// fatal: loading tolerates an incomplete dump and returns the canonical
/// session found in runtime.json.
/// </summary>
public static class RuntimeAuditSerializer
{
    public static JsonSerializerOptions JsonOptions { get; } = CreateJsonOptions();

    public static async Task WriteAsync(
        RuntimeAuditSession session,
        string directory,
        CancellationToken cancellationToken = default,
        DateTime? generatedAtUtc = null)
    {
        Directory.CreateDirectory(directory);
        // Capture one timestamp for the whole derived dump. Tests and tooling
        // may inject it to produce reproducible golden projections; production
        // callers retain wall-clock generation time by default.
        var dumpGeneratedAtUtc = generatedAtUtc ?? DateTime.UtcNow;
        var sessionFile = Path.Combine(directory, RuntimeAuditFormat.RuntimeJson);
        await File.WriteAllTextAsync(
            sessionFile,
            JsonSerializer.Serialize(session, JsonOptions),
            cancellationToken).ConfigureAwait(false);

        await WriteProjectionAsync(
            directory,
            RuntimeAuditFormat.ResourcesJson,
            session,
            [RuntimeAuditCategory.CharacterComposition, RuntimeAuditCategory.RuntimeTexture],
            dumpGeneratedAtUtc,
            cancellationToken).ConfigureAwait(false);

        await WriteProjectionAsync(
            directory,
            RuntimeAuditFormat.MaterialsJson,
            session,
            [RuntimeAuditCategory.Material],
            dumpGeneratedAtUtc,
            cancellationToken).ConfigureAwait(false);

        await WriteProjectionAsync(
            directory,
            RuntimeAuditFormat.SkeletonsJson,
            session,
            [RuntimeAuditCategory.SkeletonEvidence],
            dumpGeneratedAtUtc,
            cancellationToken).ConfigureAwait(false);

        await WriteProjectionAsync(
            directory,
            RuntimeAuditFormat.DeformersJson,
            session,
            [RuntimeAuditCategory.Deformer],
            dumpGeneratedAtUtc,
            cancellationToken).ConfigureAwait(false);

        await WriteReportAsync(session, directory, dumpGeneratedAtUtc, cancellationToken).ConfigureAwait(false);

        await File.WriteAllTextAsync(
            Path.Combine(directory, RuntimeAuditFormat.SnapshotJson),
            JsonSerializer.Serialize(RuntimeCharacterSnapshotBuilder.Build(session), JsonOptions),
            cancellationToken).ConfigureAwait(false);
    }

    public static async Task<RuntimeAuditSession?> LoadAsync(
        string directory,
        CancellationToken cancellationToken = default)
    {
        var sessionFile = Path.Combine(directory, RuntimeAuditFormat.RuntimeJson);
        if (!File.Exists(sessionFile))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(sessionFile);
            return await JsonSerializer
                         .DeserializeAsync<RuntimeAuditSession>(stream, JsonOptions, cancellationToken)
                         .ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Fail closed: an unreadable dump must never crash the game or the
            // diff tooling. The caller decides how to surface the problem.
            return null;
        }
    }

    public static async Task WriteDiffAsync(
        RuntimeAuditDiffResult diff,
        string beforeCaptureId,
        string afterCaptureId,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        var payload = new Dictionary<string, object?>
        {
            ["format"] = RuntimeAuditFormat.FormatMarker,
            ["schemaVersion"] = RuntimeAuditFormat.SchemaVersion,
            ["generatedAtUtc"] = DateTime.UtcNow.ToString("O"),
            ["before"] = beforeCaptureId,
            ["after"] = afterCaptureId,
            ["added"] = diff.Added,
            ["changed"] = diff.Changed,
            ["removed"] = diff.Removed,
            ["summary"] = diff.Summarize()
        };
        var directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(
            filePath,
            JsonSerializer.Serialize(payload, JsonOptions),
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteProjectionAsync(
        string directory,
        string fileName,
        RuntimeAuditSession session,
        RuntimeAuditCategory[] categories,
        DateTime generatedAtUtc,
        CancellationToken cancellationToken)
    {
        var categorySet = categories.ToHashSet();
        var observations = session.Observations
                                  .Where(o => ToCategory(o.Category) is { } category &&
                                              categorySet.Contains(category))
                                  .ToList();
        var payload = new Dictionary<string, object?>
        {
            ["format"] = RuntimeAuditFormat.FormatMarker,
            ["schemaVersion"] = RuntimeAuditFormat.SchemaVersion,
            ["generatedAtUtc"] = generatedAtUtc.ToUniversalTime().ToString("O"),
            ["captureId"] = session.CaptureId,
            ["categories"] = categories.Select(c => c.ToString()).ToArray(),
            ["observations"] = observations
        };
        await File.WriteAllTextAsync(
            Path.Combine(directory, fileName),
            JsonSerializer.Serialize(payload, JsonOptions),
            cancellationToken).ConfigureAwait(false);
    }

    private static RuntimeAuditCategory? ToCategory(string? category)
    {
        return Enum.TryParse<RuntimeAuditCategory>(category, out var value)
            ? value
            : null;
    }

    private static async Task WriteReportAsync(
        RuntimeAuditSession session,
        string directory,
        DateTime generatedAtUtc,
        CancellationToken cancellationToken)
    {
        var report = RuntimeAuditReport.Build(session, generatedAtUtc);
        await File.WriteAllTextAsync(
            Path.Combine(directory, RuntimeAuditFormat.ReportJson),
            JsonSerializer.Serialize(report, JsonOptions),
            cancellationToken).ConfigureAwait(false);
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
