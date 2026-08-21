namespace Meddle.Utils.Export.Ue;

public sealed class UeResourceManifest
{
    public int SchemaVersion { get; set; } = UeExportSchema.CurrentVersion;
    public List<UeResourceEntry> Resources { get; set; } = [];
    public List<UeExportObservation> Observations { get; set; } = [];
}

public sealed class UeResourceEntry
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "unknown";
    public UeResourceAvailability Availability { get; set; } = UeResourceAvailability.Available;
    public string? LogicalPath { get; set; }
    public string? ResolvedPath { get; set; }
    public string? File { get; set; }
    public string? Sha256 { get; set; }
    public long? Size { get; set; }
    public string Origin { get; set; } = "unknown";
    public string? Reason { get; set; }
    public UeRuntimeTextureMetadata? RuntimeTexture { get; set; }
    public List<UeResourceAlias> Aliases { get; set; } = [];
}

public sealed class UeResourceAlias
{
    public string? LogicalPath { get; set; }
    public string? ResolvedPath { get; set; }
    public string Origin { get; set; } = "unknown";
}

public sealed class UeRuntimeTextureMetadata
{
    public string Encoding { get; set; } = "png";
    public int Width { get; set; }
    public int Height { get; set; }
    public string? Source { get; set; }
    public string? LogicalPath { get; set; }
    public string? ResolvedPath { get; set; }
    public string? FallbackReason { get; set; }
}

public sealed class UeExportObservation
{
    public string Code { get; set; } = "";

    public string? Message { get; set; }

    public string? Reason { get; set; }

    public string? ResourceType { get; set; }

    public string? LogicalPath { get; set; }

    public string? ResolvedPath { get; set; }

    public string? ResourceId { get; set; }

    public string? NodeId { get; set; }

    public string? ModelId { get; set; }

    public int? MaterialIndex { get; set; }

    public int? PartialSkeletonIndex { get; set; }
}
