namespace Meddle.Utils.Export.Ue;

public sealed class UeCharacterSnapshot
{
    public int SchemaVersion { get; set; } = UeExportSchema.CurrentVersion;
    public UeGeneratorSnapshot? Generator { get; set; } = new();
    public List<string> KnownCaptureGaps { get; set; } =
    [
        "SkinMaterialConstantBuffer",
        "HairRenderMaterialFlags"
    ];
    public List<string> CaptureConditions { get; set; } =
    [
        "DeformerRuntimeObservationRequiresHumanCreateDeformerHookExecution"
    ];

    public UeCharacterNodeSnapshot? Root { get; set; }
}

public sealed class UeCharacterNodeSnapshot
{
    public string NodeId { get; set; } = "";
    public UeCaptureSnapshot? Capture { get; set; } = new();
    public UeCustomizeSnapshot? Customize { get; set; }
    public List<UeModelSnapshot> Models { get; set; } = [];
    public UeSkeletonSnapshot? Skeleton { get; set; }
    public UeAttachSnapshot? Attach { get; set; }
    public List<UeCharacterNodeSnapshot> Attaches { get; set; } = [];
}

public sealed class UeGeneratorSnapshot
{
    public string Name { get; set; } = "Meddle-UE";
    public string? Version { get; set; }
    public string? Commit { get; set; }
    public bool? Dirty { get; set; }
    public string? BuildId { get; set; }
}

public sealed class UeCaptureSnapshot
{
    public DateTime CapturedAtUtc { get; set; }
    public string? CharacterName { get; set; }
    public string? ObjectKind { get; set; }
    public string? ModelType { get; set; }
    public int? GenderRace { get; set; }
}

public sealed class UeCustomizeSnapshot
{
    public UeCustomizeDataSnapshot? Data { get; set; }
    public UeCustomizeParameterSnapshot? Parameters { get; set; }
    public List<UeEquipmentModelIdSnapshot> EquipmentModelIds { get; set; } = [];
}

public sealed class UeCustomizeDataSnapshot
{
    public bool LipStick { get; set; }
    public bool Highlights { get; set; }
    public bool FacePaintReversed { get; set; }
}

public sealed class UeCustomizeParameterSnapshot
{
    public float[] SkinColor { get; set; } = [];
    public float MuscleTone { get; set; }
    public float[] LipColor { get; set; } = [];
    public float[] MainColor { get; set; } = [];
    public float FacePaintUvMultiplier { get; set; }
    public float[] MeshColor { get; set; } = [];
    public float FacePaintUvOffset { get; set; }
    public float[] LeftColor { get; set; } = [];
    public float[] RightColor { get; set; } = [];
    public float[] OptionColor { get; set; } = [];
    public float[] DecalColor { get; set; } = [];
}

public sealed class UeEquipmentModelIdSnapshot
{
    public int Slot { get; set; }
    public ushort Id { get; set; }
    public byte Variant { get; set; }
    public byte Stain0 { get; set; }
    public byte Stain1 { get; set; }
    public ulong Value { get; set; }
}

public sealed class UeModelSnapshot
{
    public string Id { get; set; } = "";
    public uint SlotIndex { get; set; }
    public string? ModelType { get; set; }
    public bool Enabled { get; set; }
    public string? LogicalPath { get; set; }
    public string? ResolvedPath { get; set; }
    public string? ResourceId { get; set; }
    public UeStainSnapshot? Stain0 { get; set; }
    public UeStainSnapshot? Stain1 { get; set; }
    public UeShapeAttributeSnapshot? ShapeAttributes { get; set; }
    public UeDeformerSnapshot? Deformation { get; set; }
    public List<UeMaterialSnapshot?> Materials { get; set; } = [];
}

public sealed class UeStainSnapshot
{
    public uint SeColor { get; set; }
    public float[] Color { get; set; } = [];
    public uint RowId { get; set; }
    public string? Name { get; set; }
    public uint Shade { get; set; }
    public uint SubOrder { get; set; }
}

public sealed class UeShapeAttributeSnapshot
{
    public uint EnabledShapeMask { get; set; }
    public uint EnabledAttributeMask { get; set; }
    public List<UeNamedMaskSnapshot> ShapeMasks { get; set; } = [];
    public List<UeNamedMaskSnapshot> AttributeMasks { get; set; } = [];
}

public sealed class UeNamedMaskSnapshot
{
    public string Name { get; set; } = "";
    public short Id { get; set; }
}

public sealed class UeDeformerSnapshot
{
    public string Status { get; set; } = UeDeformerStatus.NotObserved;
    public string? PbdResourcePath { get; set; }
    public int? RaceSexId { get; set; }
    public int? DeformerId { get; set; }
    public UePbdResourceSnapshot? PbdResource { get; set; }
}

public sealed class UePbdResourceSnapshot
{
    public string? ResourceId { get; set; }
    public UeResourceAvailability Availability { get; set; } = UeResourceAvailability.Unavailable;
}

public sealed class UeMaterialSnapshot
{
    public string? LogicalPath { get; set; }
    public string? ResolvedPath { get; set; }
    public string? ResourceId { get; set; }
    public string? ShaderPackageLogicalPath { get; set; }
    public string? ShaderPackageResolvedPath { get; set; }
    public string? ShaderPackageResourceId { get; set; }
    public UeStainSnapshot? Stain0 { get; set; }
    public UeStainSnapshot? Stain1 { get; set; }
    public UeColorTableSetSnapshot? ColorTable { get; set; }
    public List<UeTextureSnapshot> Textures { get; set; } = [];
    public UeRenderOverrideSnapshot? RenderOverrides { get; set; }
}

public sealed class UeTextureSnapshot
{
    public string? LogicalPath { get; set; }
    public string? ResolvedPath { get; set; }
    public string? ResourceId { get; set; }
    public UeTextureRuntimeMetadata? RuntimeMetadata { get; set; }
}

public sealed class UeTextureRuntimeMetadata
{
    public string? Format { get; set; }
    public uint Width { get; set; }
    public uint Height { get; set; }
    public uint MipLevels { get; set; }
    public uint ArraySize { get; set; }
    public string? Dimension { get; set; }
    public string? MiscFlags { get; set; }
}

public sealed class UeRenderOverrideSnapshot
{
    public string? DecalTexturePath { get; set; }
    public string? DecalTextureLogicalPath { get; set; }
    public string? DecalTextureResolvedPath { get; set; }
    public string? DecalTextureFallbackReason { get; set; }
    public string? DecalTextureResourceId { get; set; }
    public float[]? DecalTextureColor { get; set; }
    public List<UeSkinMaterialTextureSnapshot> SkinMaterialTextures { get; set; } = [];
}

public sealed class UeSkinMaterialTextureSnapshot
{
    public string? LogicalPath { get; set; }
    public string? ResolvedPath { get; set; }
    public string? ResourceId { get; set; }
    public uint SamplerFlags { get; set; }
    public uint TargetSamplerCrc { get; set; }
}

public sealed class UeSkeletonSnapshot
{
    /// <summary>
    /// The skeleton's local transform relative to its owner (local-to-parent),
    /// not a world/instance placement. World placement belongs to the layout
    /// instance and is composed separately.
    /// </summary>
    public UeTransformSnapshot? Transform { get; set; }
    public List<UePartialSkeletonSnapshot> PartialSkeletons { get; set; } = [];
}

public sealed class UePartialSkeletonSnapshot
{
    public int Index { get; set; }
    public string? LogicalPath { get; set; }
    public string? ResolvedPath { get; set; }
    public string? ResourceId { get; set; }
    public uint PartialSkeletonFlagsRaw { get; set; }
    public int ConnectedBoneIndex { get; set; }
    public uint BoneCount { get; set; }
    public UeRuntimeSkeletonSnapshot? RuntimeSkeleton { get; set; }
    public List<UeRuntimePoseSnapshot> RuntimePoses { get; set; } = [];
}

public sealed class UeRuntimeSkeletonSnapshot
{
    public List<string?> BoneNames { get; set; } = [];
    public List<short> BoneParents { get; set; } = [];
}

public sealed class UeRuntimePoseSnapshot
{
    public List<UeTransformSnapshot> Transforms { get; set; } = [];
}

public sealed class UeTransformSnapshot
{
    // Scale/Rotation/Translation, local-to-parent. Consumers must not interpret
    // these arrays as an absolute world matrix.
    public float[] Translation { get; set; } = [];
    public float[] Rotation { get; set; } = [];
    public float[] Scale { get; set; } = [];
}

public sealed class UeAttachSnapshot
{
    public int AttachmentCount { get; set; }
    public int ExecuteType { get; set; }
    /// <summary>
    /// Local runtime attachment offset applied at the attach point. It is a
    /// local-to-parent transform, distinct from the owner bone pose and from
    /// any instance/world placement.
    /// </summary>
    public UeTransformSnapshot? OffsetTransform { get; set; }
    public int PartialSkeletonIdx { get; set; }
    public uint BoneIdx { get; set; }
    public UeSkeletonSnapshot? OwnerSkeleton { get; set; }
    public UeSkeletonSnapshot? TargetSkeleton { get; set; }
}
