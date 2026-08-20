namespace Meddle.Utils.Export.Ue;

public sealed class UeResourceCandidate
{
    public required UeResourceType Type { get; init; }
    public string? LogicalPath { get; init; }
    public string? ResolvedPath { get; init; }
    public required byte[] Data { get; init; }
    public UeResourceOrigin Origin { get; init; } = UeResourceOrigin.Unknown;
    public UeRuntimeTextureMetadata? RuntimeTexture { get; init; }
}

public readonly record struct UeResourceReference(string Id, string? File);

public sealed class UeResourceBundle
{
    public required UeResourceManifest Manifest { get; init; }
    public required IReadOnlyDictionary<string, byte[]> DataById { get; init; }
}

public sealed class UeResourceManifestBuilder
{
    private readonly Dictionary<string, UeResourceEntry> entries = new();
    private readonly Dictionary<string, byte[]> dataById = new();
    private readonly List<UeExportObservation> observations = [];
    private readonly UeResourceFileNameBuilder resourceFileNameBuilder = new();

    public UeResourceReference Add(UeResourceCandidate candidate)
    {
        var sha256 = UeResourceIdentity.GetSha256(candidate.Data);
        var runtimeTexture = candidate.RuntimeTexture;
        var id = UeResourceIdentity.CreateResourceId(sha256, candidate.Type, runtimeTexture);
        var runtimeKind = runtimeTexture == null
            ? null
            : string.IsNullOrWhiteSpace(runtimeTexture.Source)
                ? "texture"
                : runtimeTexture.Source;

        if (!entries.TryGetValue(id, out var entry))
        {
            var fileName = resourceFileNameBuilder.Build(
                candidate.Type,
                candidate.ResolvedPath ?? candidate.LogicalPath,
                sha256,
                fallbackPath: candidate.LogicalPath,
                runtimeKind: runtimeKind);
            entry = new UeResourceEntry
            {
                Id = id,
                Type = ToJsonType(candidate.Type),
                Availability = UeResourceAvailability.Available,
                LogicalPath = candidate.LogicalPath,
                ResolvedPath = candidate.ResolvedPath,
                File = $"resources/{fileName}",
                Sha256 = sha256,
                Size = candidate.Data.LongLength,
                Origin = ToJsonOrigin(candidate.Origin),
                Reason = null,
                RuntimeTexture = runtimeTexture,
                Aliases = []
            };
            entries[id] = entry;
            dataById[id] = candidate.Data;
        }
        else
        {
            AddAlias(entry, candidate);
        }

        return new UeResourceReference(id, entry.File);
    }

    public UeResourceReference AddObservation(
        string? logicalPath,
        string? resolvedPath,
        UeResourceType type,
        UeResourceOrigin origin,
        UeResourceAvailability availability,
        string? reason = null)
    {
        if (string.IsNullOrWhiteSpace(logicalPath) && string.IsNullOrWhiteSpace(resolvedPath))
        {
            throw new ArgumentException("An observation requires at least one logical or resolved source path.");
        }

        var id = UeResourceIdentity.CreateObservationId(logicalPath, resolvedPath, type);
        if (entries.TryGetValue(id, out var entry))
        {
            var originJson = ToJsonOrigin(origin);
            if (entry.Availability != availability || !string.Equals(entry.Origin, originJson, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Observation '{id}' was already registered with availability '{entry.Availability}' and origin '{entry.Origin}'.");
            }
        }
        else
        {
            entry = new UeResourceEntry
            {
                Id = id,
                Type = ToJsonType(type),
                Availability = availability,
                LogicalPath = logicalPath,
                ResolvedPath = resolvedPath,
                File = null,
                Sha256 = null,
                Size = null,
                Origin = ToJsonOrigin(origin),
                Reason = reason,
                RuntimeTexture = null,
                Aliases = []
            };
            entries[id] = entry;
        }

        return new UeResourceReference(id, null);
    }

    public void AddObservation(UeExportObservation observation)
    {
        observations.Add(observation);
    }

    public UeResourceBundle BuildBundle()
    {
        foreach (var entry in entries.Values)
        {
            entry.Aliases = entry.Aliases
                                 .OrderBy(x => x.LogicalPath ?? string.Empty, StringComparer.Ordinal)
                                 .ThenBy(x => x.ResolvedPath ?? string.Empty, StringComparer.Ordinal)
                                 .ThenBy(x => x.Origin, StringComparer.Ordinal)
                                 .ToList();
        }

        var orderedResources = entries.Values
                                      .OrderBy(x => x.Type, StringComparer.Ordinal)
                                      .ThenBy(x => x.Availability.ToString(), StringComparer.Ordinal)
                                      .ThenBy(x => x.LogicalPath ?? string.Empty, StringComparer.Ordinal)
                                      .ThenBy(x => x.ResolvedPath ?? string.Empty, StringComparer.Ordinal)
                                      .ThenBy(x => x.Sha256 ?? string.Empty, StringComparer.Ordinal)
                                      .ToList();

        var manifest = new UeResourceManifest
        {
            Resources = orderedResources,
            Observations = [.. observations]
        };

        return new UeResourceBundle
        {
            Manifest = manifest,
            DataById = dataById
        };
    }

    private static void AddAlias(UeResourceEntry entry, UeResourceCandidate candidate)
    {
        if (entry.LogicalPath == candidate.LogicalPath &&
            entry.ResolvedPath == candidate.ResolvedPath &&
            entry.Origin == ToJsonOrigin(candidate.Origin))
        {
            return;
        }

        var alias = new UeResourceAlias
        {
            LogicalPath = candidate.LogicalPath,
            ResolvedPath = candidate.ResolvedPath,
            Origin = ToJsonOrigin(candidate.Origin)
        };
        if (entry.Aliases.Any(x => x.LogicalPath == alias.LogicalPath &&
                                   x.ResolvedPath == alias.ResolvedPath &&
                                   x.Origin == alias.Origin))
        {
            return;
        }

        entry.Aliases.Add(alias);
    }

    private static string ToJsonType(UeResourceType type)
    {
        return type.ToString().ToLowerInvariant();
    }

    private static string ToJsonOrigin(UeResourceOrigin origin)
    {
        return origin.ToString().ToLowerInvariant();
    }
}
