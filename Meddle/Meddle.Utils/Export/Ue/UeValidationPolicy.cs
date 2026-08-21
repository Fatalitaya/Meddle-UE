namespace Meddle.Utils.Export.Ue;

public sealed record UeValidationIssue(
    string ResourceId,
    string ResourceType,
    string? LogicalPath,
    string? ResolvedPath,
    UeResourceAvailability Availability,
    string Message);

public sealed record UeValidationResult(
    bool IsSatisfied,
    IReadOnlyList<UeValidationIssue> Issues)
{
    public static UeValidationResult Satisfied { get; } = new(true, []);
}

public sealed class UeValidationPolicy
{
    private readonly Func<UeResourceEntry, bool>? requirement;
    private readonly IReadOnlySet<string>? resourceIds;

    public static UeValidationPolicy StructuralOnly { get; } = new();

    public UeValidationPolicy()
    {
    }

    public UeValidationPolicy(Func<UeResourceEntry, bool> requirement)
    {
        this.requirement = requirement ?? throw new ArgumentNullException(nameof(requirement));
    }

    private UeValidationPolicy(
        Func<UeResourceEntry, bool> requirement,
        IReadOnlySet<string> resourceIds)
    {
        this.requirement = requirement;
        this.resourceIds = resourceIds;
    }

    public static UeValidationPolicy ForResourceIds(params string[] ids)
    {
        var resourceIds = new HashSet<string>(ids ?? [], StringComparer.Ordinal);
        return new UeValidationPolicy(
            entry => resourceIds.Contains(entry.Id),
            resourceIds);
    }

    public static UeValidationPolicy ForResourceTypes(params UeResourceType[] types)
    {
        var resourceTypes = new HashSet<UeResourceType>(types ?? []);
        return new UeValidationPolicy(entry =>
            Enum.TryParse<UeResourceType>(entry.Type, ignoreCase: true, out var type) &&
            resourceTypes.Contains(type));
    }

    public static UeValidationPolicy FromRequirement(Func<UeResourceEntry, bool> requirement)
    {
        return new UeValidationPolicy(requirement);
    }

    public UeValidationResult Evaluate(
        UeCharacterSnapshot snapshot,
        UeResourceManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(manifest);

        var issues = new List<UeValidationIssue>();
        var reportedIds = new HashSet<string>(StringComparer.Ordinal);
        if (resourceIds != null)
        {
            var manifestById = manifest.Resources
                                       .GroupBy(x => x.Id, StringComparer.Ordinal)
                                       .ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);
            foreach (var id in resourceIds)
            {
                if (manifestById.TryGetValue(id, out var entry))
                {
                    AddIssueIfUnavailable(entry, issues);
                    reportedIds.Add(id);
                }
                else
                {
                    issues.Add(new UeValidationIssue(
                        id,
                        "unknown",
                        null,
                        null,
                        UeResourceAvailability.Unavailable,
                        $"Consumer-required resource id '{id}' was not observed in the manifest."));
                    reportedIds.Add(id);
                }
            }
        }

        if (requirement != null)
        {
            foreach (var entry in manifest.Resources)
            {
                if (requirement(entry) && !reportedIds.Contains(entry.Id))
                {
                    AddIssueIfUnavailable(entry, issues);
                }
            }
        }

        return issues.Count == 0 ? UeValidationResult.Satisfied : new UeValidationResult(false, issues);
    }

    public UeValidationResult Evaluate(
        UeCharacterSnapshot snapshot,
        UeResourceManifest manifest,
        string outputDirectory)
    {
        UeExportValidator.Validate(snapshot, manifest, outputDirectory);
        return Evaluate(snapshot, manifest);
    }

    private static void AddIssueIfUnavailable(
        UeResourceEntry entry,
        List<UeValidationIssue> issues)
    {
        if (entry.Availability == UeResourceAvailability.Available)
        {
            return;
        }

        issues.Add(new UeValidationIssue(
            entry.Id,
            entry.Type,
            entry.LogicalPath,
            entry.ResolvedPath,
            entry.Availability,
            $"Resource '{entry.Id}' is {entry.Availability}; this consumer policy requires captured bytes."));
    }
}
