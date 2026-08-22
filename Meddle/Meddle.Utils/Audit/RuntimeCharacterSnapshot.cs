using System.Text.Json.Serialization;

namespace Meddle.Utils.Audit;

/// <summary>A stable, observation-only contract for one resolved runtime character.</summary>
public sealed class RuntimeCharacterSnapshot
{
    public const string Contract = "Meddle Runtime Character Snapshot";
    public const int CurrentSchemaVersion = 1;

    public string Format => Contract;
    public int SchemaVersion => CurrentSchemaVersion;
    public required string CaptureId { get; init; }
    public required DateTime CapturedAtUtc { get; init; }
    public required ActorIdentity Actor { get; init; }
    public required CustomizeState Customize { get; init; }
    public required AppearanceState Appearance { get; init; }
    public required List<ResourceBinding> Resources { get; init; }
    public required SkeletonReference Skeleton { get; init; }
}

public sealed class ActorIdentity
{
    public required string RuntimeId { get; init; }
    public string? Name { get; init; }
    public string? ObjectKind { get; init; }
    public object? ObjectId { get; init; }
    public object? DataId { get; init; }
    public object? ModelId { get; init; }
    public object? ModelType { get; init; }
}

public sealed class CustomizeState
{
    public bool Available { get; init; }
    public Dictionary<string, object?> Values { get; init; } = new();
}

public sealed class AppearanceState
{
    public List<EquipmentAppearanceState> Equipment { get; init; } = [];
    public List<AppearanceComponent> Components { get; init; } = [];
    public required VisorRuntimeState Visor { get; init; }
    public required CharacterAttachmentGraph Attachments { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EquipmentHideReason { Visible, HiddenByRuntimeFlag, HiddenUnknown }

public sealed class EquipmentAppearanceState
{
    public object? Slot { get; init; }
    public string? SlotName { get; init; }
    public bool Equipped { get; init; }
    public bool? Visible { get; init; }
    public bool? Hidden { get; init; }
    public object? RuntimeFlags { get; init; }
    public string? ModelResource { get; init; }
    public EquipmentHideReason? HideReason { get; init; }
    public string Source { get; init; } = "runtime";
}

public sealed class AppearanceComponent
{
    public required string Slot { get; init; }
    public bool? Visible { get; init; }
    public string? Resource { get; init; }
    public object? Model { get; init; }
    public object? Materials { get; init; }
    public AttachmentReference? Attachment { get; init; }
}

public sealed class VisorRuntimeState
{
    public bool? Supported { get; init; }
    public bool? Active { get; init; }
    public string State { get; init; } = "unknown";
    public List<string> AffectedObjects { get; init; } = [];
}

public sealed class CharacterAttachmentGraph
{
    public List<AttachmentNode> Nodes { get; init; } = [];
    public List<AttachmentEdge> Edges { get; init; } = [];
}

public sealed class AttachmentNode
{
    public required string Id { get; init; }
    public required string Type { get; init; }
    public string? Resource { get; init; }
    public string? Parent { get; init; }
}

public sealed class AttachmentEdge
{
    public required string Parent { get; init; }
    public required string Child { get; init; }
    public required string Relation { get; init; }
    public AttachmentReference? Attachment { get; init; }
}

public sealed class AttachmentReference
{
    public int? BoneIndex { get; init; }
    public string? BoneNameIfAvailable { get; init; }
    public object? Transform { get; init; }
}

public sealed class ResourceBinding
{
    public string? LogicalPath { get; init; }
    public string? ResolvedPath { get; init; }
    public bool? OverrideDetected { get; init; }
    public string Provenance { get; init; } = "unknown";
}

public sealed class SkeletonReference
{
    public bool Available { get; init; }
    public object? RuntimeAddress { get; init; }
    public List<object?> Resources { get; init; } = [];
}

/// <summary>Optional, conservative mapping for runtime-provided stable bone semantics.</summary>
public static class FFXIVBoneSemanticMap
{
    private static readonly IReadOnlyDictionary<int, string> Known = new Dictionary<int, string>();

    public static string? TryGetSemanticName(int boneIndex) =>
        Known.TryGetValue(boneIndex, out var name) ? name : null;
}
