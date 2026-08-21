using System.Numerics;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using Meddle.Plugin.Models;
using Meddle.Plugin.Models.Layout;
using Meddle.Plugin.Models.Skeletons;
using Meddle.Plugin.Utils;
using Meddle.Utils.Constants;
using Meddle.Utils.Export;
using Meddle.Utils.Export.Ue;

namespace Meddle.Plugin.Export.Ue;

public sealed class UeSnapshotBuilder
{
    private readonly IUeResourceReader resourceReader;
    private readonly CancellationToken cancellationToken;
    private readonly UeResourceManifestBuilder resourceManifest = new();
    private readonly List<UeExportObservation> observations = [];
    private readonly Dictionary<string, (byte[] Data, UeResourceOrigin Origin)> resourceReadCache = new(StringComparer.Ordinal);

    private sealed record UePathResourceResult(
        UeResourceReference? Reference,
        UeResourceReadStatus Status,
        byte[]? Data = null);

    public UeSnapshotBuilder(
        IUeResourceReader resourceReader,
        CancellationToken cancellationToken)
    {
        this.resourceReader = resourceReader;
        this.cancellationToken = cancellationToken;
    }

    public IReadOnlyList<UeExportObservation> Observations => observations;

    public UeCharacterSnapshot? Build(
        ParsedCharacterInfo root,
        string? displayName,
        string? objectKind)
    {
        var rootNode = BuildCharacter(
            root,
            nodeId: "root",
            isRoot: true,
            displayName,
            objectKind,
            new HashSet<ParsedCharacterInfo>(ReferenceEqualityComparer.Instance));
        if (rootNode == null)
        {
            return null;
        }

        var provenance = BuildProvenance.Current;
        return new UeCharacterSnapshot
        {
            SchemaVersion = UeExportSchema.CurrentVersion,
            Generator = new UeGeneratorSnapshot
            {
                Version = provenance.Version,
                Commit = provenance.Commit,
                Dirty = provenance.Dirty,
                BuildId = provenance.BuildId
            },
            Root = rootNode
        };
    }

    public UeResourceBundle BuildBundle()
    {
        return resourceManifest.BuildBundle();
    }

    private UeCharacterNodeSnapshot? BuildCharacter(
        ParsedCharacterInfo info,
        string nodeId,
        bool isRoot,
        string? displayName,
        string? objectKind,
        HashSet<ParsedCharacterInfo> activePath)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!activePath.Add(info))
        {
            return null;
        }

        try
        {
            var snapshot = new UeCharacterNodeSnapshot
            {
                NodeId = nodeId,
                Capture = new UeCaptureSnapshot
                {
                    CapturedAtUtc = ToUtc(info.ParsedAt),
                    CharacterName = isRoot ? displayName : null,
                    ObjectKind = isRoot ? objectKind : null,
                    ModelType = info.ModelType.ToString(),
                    GenderRace = info.ModelType == CharacterBase.ModelType.Human &&
                                info.GenderRace != GenderRace.Unknown
                        ? (int)info.GenderRace
                        : null
                },
                Customize = BuildCustomize(info),
                Skeleton = BuildSkeleton(info.Skeleton, nodeId),
                Attach = BuildAttach(info.Attach, nodeId)
            };

            for (var modelIndex = 0; modelIndex < info.Models.Count; modelIndex++)
            {
                snapshot.Models.Add(BuildModel(info.Models[modelIndex], modelIndex, nodeId));
            }

            for (var attachIndex = 0; attachIndex < info.Attaches.Count; attachIndex++)
            {
                var child = BuildCharacter(
                    info.Attaches[attachIndex],
                    nodeId: $"{nodeId}/attach-{attachIndex}",
                    isRoot: false,
                    null,
                    null,
                    activePath);
                if (child != null)
                {
                    snapshot.Attaches.Add(child);
                }
            }

            return snapshot;
        }
        finally
        {
            activePath.Remove(info);
        }
    }

    private UeCustomizeSnapshot? BuildCustomize(ParsedCharacterInfo info)
    {
        var data = info.CustomizeData;
        var parameters = info.CustomizeParameter;
        if (data == null && parameters == null)
        {
            return null;
        }

        var snapshot = new UeCustomizeSnapshot();
        if (data != null)
        {
            snapshot.Data = new UeCustomizeDataSnapshot
            {
                LipStick = data.LipStick,
                Highlights = data.Highlights,
                FacePaintReversed = data.FacePaintReversed
            };
        }

        if (parameters != null)
        {
            snapshot.Parameters = new UeCustomizeParameterSnapshot
            {
                SkinColor = AsFloatArray(parameters.SkinColor),
                MuscleTone = parameters.MuscleTone,
                LipColor = AsFloatArray(parameters.LipColor),
                MainColor = AsFloatArray(parameters.MainColor),
                FacePaintUvMultiplier = parameters.FacePaintUvMultiplier,
                MeshColor = AsFloatArray(parameters.MeshColor),
                FacePaintUvOffset = parameters.FacePaintUvOffset,
                LeftColor = AsFloatArray(parameters.LeftColor),
                RightColor = AsFloatArray(parameters.RightColor),
                OptionColor = AsFloatArray(parameters.OptionColor),
                DecalColor = AsFloatArray(parameters.DecalColor)
            };
        }

        var equipmentIds = info.EquipmentModelIds;
        for (var i = 0; i < equipmentIds.Count; i++)
        {
            var equipment = equipmentIds[i];
            snapshot.EquipmentModelIds.Add(new UeEquipmentModelIdSnapshot
            {
                Slot = i,
                Id = equipment.Id,
                Variant = equipment.Variant,
                Stain0 = equipment.Stain0,
                Stain1 = equipment.Stain1,
                Value = equipment.Value
            });
        }

        return snapshot;
    }

    private UeModelSnapshot BuildModel(
        ParsedModelInfo model,
        int modelIndex,
        string nodeId)
    {
        var modelId = $"{nodeId}/model-{modelIndex}";
        var modelReference = RegisterPathResource(
            model.Path.GamePath,
            model.Path.FullPath,
            UeResourceType.Mdl,
            modelId,
            nodeId: nodeId);
        var snapshot = new UeModelSnapshot
        {
            Id = modelId,
            SlotIndex = model.SlotIndex,
            ModelType = model.ModelType?.ToString(),
            Enabled = model.Enabled,
            LogicalPath = model.Path.GamePath,
            ResolvedPath = model.Path.FullPath,
            ResourceId = modelReference?.Id,
            Stain0 = model.Stain0 == null ? null : ToStain(model.Stain0),
            Stain1 = model.Stain1 == null ? null : ToStain(model.Stain1),
            ShapeAttributes = ToShapeAttributes(model.ShapeAttributeGroup),
            Deformation = ToDeformation(model, modelId, nodeId)
        };

        for (var materialIndex = 0; materialIndex < model.Materials.Length; materialIndex++)
        {
            var material = model.Materials[materialIndex];
            snapshot.Materials.Add(
                material == null
                    ? null
                    : BuildMaterial(material, modelId, nodeId, materialIndex));
        }

        return snapshot;
    }

    private UeMaterialSnapshot? BuildMaterial(
        ParsedMaterialInfo material,
        string modelId,
        string nodeId,
        int materialIndex)
    {
        var materialReference = RegisterPathResource(
            material.Path.GamePath,
            material.Path.FullPath,
            UeResourceType.Mtrl,
            modelId,
            materialIndex,
            nodeId: nodeId);
        var shpkPath = string.IsNullOrWhiteSpace(material.Shpk)
            ? null
            : $"shader/sm5/shpk/{material.Shpk}";
        var shpkReference = string.IsNullOrWhiteSpace(shpkPath)
            ? null
            : RegisterPathResource(
                shpkPath,
                shpkPath,
                UeResourceType.Shpk,
                modelId,
                materialIndex,
                nodeId: nodeId);

        var snapshot = new UeMaterialSnapshot
        {
            LogicalPath = material.Path.GamePath,
            ResolvedPath = material.Path.FullPath,
            ResourceId = materialReference?.Id,
            ShaderPackageLogicalPath = shpkPath,
            ShaderPackageResolvedPath = shpkPath,
            ShaderPackageResourceId = shpkReference?.Id,
            Stain0 = material.Stain0 == null ? null : ToStain(material.Stain0),
            Stain1 = material.Stain1 == null ? null : ToStain(material.Stain1),
            ColorTable = UeColorTableSnapshotMapper.Map(material.ColorTable),
            RenderOverrides = material.RenderMaterialOutput == null
                ? null
                : BuildRenderOverrides(material.RenderMaterialOutput, modelId, nodeId, materialIndex)
        };

        for (var textureIndex = 0; textureIndex < material.Textures.Length; textureIndex++)
        {
            var texture = material.Textures[textureIndex];
            var textureReference = RegisterPathResource(
                texture.Path.GamePath,
                texture.Path.FullPath,
                UeResourceType.Tex,
                modelId,
                materialIndex,
                nodeId: nodeId);
            snapshot.Textures.Add(new UeTextureSnapshot
            {
                LogicalPath = texture.Path.GamePath,
                ResolvedPath = texture.Path.FullPath,
                ResourceId = textureReference?.Id,
                RuntimeMetadata = ToTextureRuntimeMetadata(texture.Resource)
            });
        }

        return snapshot;
    }

    private UeRenderOverrideSnapshot? BuildRenderOverrides(
        OnRenderMaterialOutput output,
        string modelId,
        string nodeId,
        int materialIndex)
    {
        var snapshot = new UeRenderOverrideSnapshot
        {
            DecalTexturePath = output.DecalTexturePath,
            DecalTextureColor = output.DecalTextureColor == null ? null : AsFloatArray(output.DecalTextureColor.Value)
        };

        if (!string.IsNullOrWhiteSpace(output.DecalTexturePath))
        {
            var hasRuntimeFallback = output.DecalTexture != null;
            var decalResult = ReadPathResource(
                output.DecalTexturePath,
                output.DecalTexturePath,
                UeResourceType.Tex,
                modelId,
                materialIndex,
                recordFailure: !hasRuntimeFallback,
                nodeId: nodeId);
            if (decalResult.Status == UeResourceReadStatus.Success && decalResult.Reference != null)
            {
                snapshot.DecalTextureResourceId = decalResult.Reference.Value.Id;
            }
            else if (hasRuntimeFallback)
            {
                snapshot.DecalTextureResourceId = AddRuntimeDecalTexture(
                    output.DecalTexture!,
                    fallbackReason: ToFallbackReason(decalResult.Status),
                    logicalPath: output.DecalTexturePath,
                    resolvedPath: output.DecalTexturePath);
                snapshot.DecalTexturePath = null;
                snapshot.DecalTextureLogicalPath = output.DecalTexturePath;
                snapshot.DecalTextureResolvedPath = output.DecalTexturePath;
                snapshot.DecalTextureFallbackReason = ToFallbackReason(decalResult.Status);
                AddObservationEvent(
                    "PathSourceReplacedByRuntimeCapture",
                    "Path-backed decal source was unavailable; runtime texture bytes were captured instead.",
                    output.DecalTexturePath,
                    output.DecalTexturePath,
                    UeResourceType.Tex,
                    snapshot.DecalTextureResourceId,
                    modelId,
                    materialIndex,
                    nodeId: nodeId);
            }
            else if (decalResult.Reference != null)
            {
                snapshot.DecalTextureResourceId = decalResult.Reference.Value.Id;
            }
        }
        else if (output.DecalTexture != null)
        {
            snapshot.DecalTextureResourceId = AddRuntimeDecalTexture(output.DecalTexture!);
        }

        foreach (var skinTexture in output.SkinMaterialTextures)
        {
            var logicalPath = skinTexture.TexturePathFromMaterial;
            var resolvedPath = skinTexture.TexturePath;
            var reference = RegisterPathResource(
                logicalPath,
                resolvedPath,
                UeResourceType.Tex,
                modelId,
                materialIndex,
                nodeId: nodeId);
            snapshot.SkinMaterialTextures.Add(new UeSkinMaterialTextureSnapshot
            {
                LogicalPath = logicalPath,
                ResolvedPath = resolvedPath,
                ResourceId = reference?.Id,
                SamplerFlags = skinTexture.SamplerFlags,
                TargetSamplerCrc = skinTexture.TargetSamplerCrc
            });
        }

        return snapshot;
    }

    private string AddRuntimeDecalTexture(
        Meddle.Utils.SkTexture texture,
        string? fallbackReason = null,
        string? logicalPath = null,
        string? resolvedPath = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var pngBytes = UeRuntimeTextureUtil.EncodePng(texture);
        var decalReference = resourceManifest.Add(new UeResourceCandidate
        {
            Type = UeResourceType.Tex,
            LogicalPath = logicalPath,
            ResolvedPath = resolvedPath,
            Data = pngBytes,
            Origin = UeResourceOrigin.RuntimeGenerated,
            RuntimeTexture = new UeRuntimeTextureMetadata
            {
                Encoding = "png",
                Width = texture.Width,
                Height = texture.Height,
                Source = "decal",
                LogicalPath = logicalPath,
                ResolvedPath = resolvedPath,
                FallbackReason = fallbackReason
            }
        });
        return decalReference.Id;
    }

    private static string? ToFallbackReason(UeResourceReadStatus status)
    {
        return status switch
        {
            UeResourceReadStatus.NotFound => "ResourceUnavailable",
            UeResourceReadStatus.ReadFailed => "ResourceReadFailed",
            UeResourceReadStatus.Unresolved => "ResourceUnresolved",
            UeResourceReadStatus.MissingPath => "MissingResourcePath",
            _ => null
        };
    }

    private UeDeformerSnapshot ToDeformation(
        ParsedModelInfo model,
        string modelId,
        string nodeId)
    {
        var isHuman = model.ModelType == CharacterBase.ModelType.Human;
        var snapshot = UeDeformerSnapshotFactory.FromObservation(model.Deformer, isHuman);

        if (snapshot.Status == UeDeformerStatus.Observed &&
            !string.IsNullOrWhiteSpace(snapshot.PbdResourcePath))
        {
            var pbdResult = ReadPathResource(
                snapshot.PbdResourcePath,
                snapshot.PbdResourcePath,
                UeResourceType.Pbd,
                modelId,
                nodeId: nodeId);
            snapshot.PbdResource = new UePbdResourceSnapshot
            {
                ResourceId = pbdResult.Reference?.Id,
                Availability =
                    pbdResult.Status == UeResourceReadStatus.Success &&
                    pbdResult.Reference != null
                        ? UeResourceAvailability.Available
                        : UeResourceReadPolicy.GetAvailability(pbdResult.Status)
            };
        }

        if (snapshot.Status == UeDeformerStatus.NotObserved)
        {
            AddObservationEvent(
                "DeformerNotObserved",
                "Human_CreateDeformer observation is not available for this model; no deformer state was inferred.",
                modelId: modelId,
                nodeId: nodeId);
        }

        return snapshot;
    }

    private UeSkeletonSnapshot BuildSkeleton(ParsedSkeleton skeleton, string nodeId)
    {
        var snapshot = new UeSkeletonSnapshot
        {
            Transform = ToTransform(skeleton.Transform)
        };

        for (var i = 0; i < skeleton.PartialSkeletons.Count; i++)
        {
            snapshot.PartialSkeletons.Add(BuildPartialSkeleton(skeleton.PartialSkeletons[i], i, nodeId));
        }

        return snapshot;
    }

    private UePartialSkeletonSnapshot BuildPartialSkeleton(
        ParsedPartialSkeleton partial,
        int index,
        string nodeId)
    {
        var partialSnapshot = new UePartialSkeletonSnapshot
        {
            Index = index,
            PartialSkeletonFlagsRaw = partial.PartialSkeletonFlagsRaw,
            ConnectedBoneIndex = partial.ConnectedBoneIndex,
            BoneCount = partial.BoneCount
        };

        if (UePartialSkeletonPolicy.IsEmptyRuntimeSlot(
                partial.HandlePath,
                partial.HkSkeleton != null,
                partial.BoneCount,
                partial.Poses.Count,
                partial.PartialSkeletonFlagsRaw,
                partial.ConnectedBoneIndex))
        {
            partialSnapshot.LogicalPath = null;
            partialSnapshot.ResolvedPath = null;
            partialSnapshot.ResourceId = null;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(partial.HandlePath))
            {
                partialSnapshot.LogicalPath = null;
                partialSnapshot.ResolvedPath = null;
                partialSnapshot.ResourceId = null;
                if (UePartialSkeletonPolicy.HasRuntimeEvidenceWithoutSource(
                        partial.HandlePath,
                        partial.HkSkeleton != null,
                        partial.BoneCount,
                        partial.Poses.Count))
                {
                    AddObservationEvent(
                        "SkeletonSourceNotObserved",
                        $"Partial skeleton {index} has runtime skeleton evidence but no HandlePath was observed.",
                        partialSkeletonIndex: index,
                        nodeId: nodeId);
                }
            }
            else
            {
                var reference = RegisterPathResource(
                    partial.HandlePath,
                    partial.HandlePath,
                    UeResourceType.Sklb,
                    partialSkeletonIndex: index,
                    nodeId: nodeId);
                partialSnapshot.LogicalPath = partial.HandlePath;
                partialSnapshot.ResolvedPath = partial.HandlePath;
                partialSnapshot.ResourceId = reference?.Id;
            }
        }

        if (partial.HkSkeleton != null)
        {
            partialSnapshot.RuntimeSkeleton = new UeRuntimeSkeletonSnapshot
            {
                BoneNames = [.. partial.HkSkeleton.BoneNames],
                BoneParents = [.. partial.HkSkeleton.BoneParents]
            };
        }

        foreach (var pose in partial.Poses)
        {
            partialSnapshot.RuntimePoses.Add(new UeRuntimePoseSnapshot
            {
                Transforms = pose.Pose.Select(ToTransform).ToList()
            });
        }

        return partialSnapshot;
    }

    private UeAttachSnapshot BuildAttach(ParsedAttach attach, string nodeId)
    {
        var snapshot = new UeAttachSnapshot
        {
            AttachmentCount = attach.AttachmentCount,
            ExecuteType = attach.ExecuteType,
            OffsetTransform = attach.OffsetTransform == null ? null : ToTransform(attach.OffsetTransform),
            PartialSkeletonIdx = attach.PartialSkeletonIdx,
            BoneIdx = attach.BoneIdx
        };

        snapshot.OwnerSkeleton = attach.OwnerSkeleton == null ? null : BuildSkeleton(attach.OwnerSkeleton, nodeId);
        snapshot.TargetSkeleton = attach.TargetSkeleton == null ? null : BuildSkeleton(attach.TargetSkeleton, nodeId);
        return snapshot;
    }

    private UeResourceReference? RegisterPathResource(
        string? logicalPath,
        string? resolvedPath,
        UeResourceType fallbackType,
        string? modelId = null,
        int? materialIndex = null,
        int? partialSkeletonIndex = null,
        bool recordFailure = true,
        string? nodeId = null)
    {
        return ReadPathResource(
            logicalPath,
            resolvedPath,
            fallbackType,
            modelId,
            materialIndex,
            partialSkeletonIndex,
            recordFailure,
            nodeId).Reference;
    }

    private UePathResourceResult ReadPathResource(
        string? logicalPath,
        string? resolvedPath,
        UeResourceType fallbackType,
        string? modelId = null,
        int? materialIndex = null,
        int? partialSkeletonIndex = null,
        bool recordFailure = true,
        string? nodeId = null)
    {
        var effectiveResolved = string.IsNullOrWhiteSpace(resolvedPath) ? logicalPath : resolvedPath;
        var type = effectiveResolved == null
            ? fallbackType
            : UeResourceIdentity.GetResourceTypeFromPath(effectiveResolved);
        if (type == UeResourceType.Unknown)
        {
            type = fallbackType;
        }

        if (string.IsNullOrWhiteSpace(effectiveResolved))
        {
            if (recordFailure)
            {
                AddObservationEvent(
                    UeResourceReadPolicy.MissingSourceObservationCode,
                    UeResourceReadPolicy.MissingSourceObservationMessage,
                    logicalPath,
                    resolvedPath,
                    type,
                    modelId: modelId,
                    materialIndex: materialIndex,
                    partialSkeletonIndex: partialSkeletonIndex,
                    nodeId: nodeId);
            }

            return new UePathResourceResult(null, UeResourceReadStatus.MissingPath);
        }

        cancellationToken.ThrowIfCancellationRequested();
        UeResourceOrigin resourceOrigin;
        (byte[] Data, UeResourceOrigin Origin) cached;
        if (!resourceReadCache.TryGetValue(effectiveResolved, out cached))
        {
            var readResult = resourceReader.Read(new UeResourceReadRequest(logicalPath, resolvedPath, fallbackType));
            if (readResult.Status != UeResourceReadStatus.Success)
            {
                var availability = UeResourceReadPolicy.GetAvailability(readResult.Status);
                var code = UeResourceReadPolicy.GetObservationCode(readResult.Status);
                UeResourceReference? observationReference = null;
                if (recordFailure)
                {
                    observationReference = resourceManifest.AddObservation(
                        logicalPath,
                        resolvedPath ?? logicalPath,
                        type,
                        readResult.Origin,
                        availability,
                        code);
                }

                if (recordFailure)
                {
                    AddObservationEvent(
                        code,
                        UeResourceReadPolicy.GetObservationMessage(readResult.Status, readResult.Error),
                        logicalPath,
                        resolvedPath ?? logicalPath,
                        type,
                        observationReference?.Id,
                        modelId,
                        materialIndex,
                        partialSkeletonIndex,
                        nodeId);
                }

                return new UePathResourceResult(
                    observationReference,
                    readResult.Status);
            }

            if (readResult.Data == null)
            {
                var code = UeResourceReadPolicy.GetObservationCode(UeResourceReadStatus.NotFound);
                UeResourceReference? observationReference = null;
                if (recordFailure)
                {
                    observationReference = resourceManifest.AddObservation(
                        logicalPath,
                        resolvedPath ?? logicalPath,
                        type,
                        readResult.Origin,
                        UeResourceAvailability.Unavailable,
                        code);
                }

                if (recordFailure)
                {
                    AddObservationEvent(
                        code,
                        UeResourceReadPolicy.GetObservationMessage(UeResourceReadStatus.NotFound),
                        logicalPath,
                        resolvedPath ?? logicalPath,
                        type,
                        observationReference?.Id,
                        modelId,
                        materialIndex,
                        partialSkeletonIndex,
                        nodeId);
                }

                return new UePathResourceResult(
                    observationReference,
                    UeResourceReadStatus.NotFound);
            }

            cached = (readResult.Data, readResult.Origin);
            resourceReadCache[effectiveResolved] = cached;
            resourceOrigin = cached.Origin;
        }
        else
        {
            resourceOrigin = cached.Origin;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var reference = resourceManifest.Add(new UeResourceCandidate
        {
            Type = type,
            LogicalPath = logicalPath,
            ResolvedPath = resolvedPath ?? logicalPath,
            Data = cached.Data,
            Origin = resourceOrigin
        });
        return new UePathResourceResult(reference, UeResourceReadStatus.Success, cached.Data);
    }

    private void AddObservationEvent(
        string code,
        string message,
        string? logicalPath = null,
        string? resolvedPath = null,
        UeResourceType? resourceType = null,
        string? resourceId = null,
        string? modelId = null,
        int? materialIndex = null,
        int? partialSkeletonIndex = null,
        string? nodeId = null)
    {
        var observation = new UeExportObservation
        {
            Code = code,
            Message = message,
            ResourceType = resourceType?.ToString().ToLowerInvariant(),
            LogicalPath = logicalPath,
            ResolvedPath = resolvedPath,
            ResourceId = resourceId,
            NodeId = nodeId,
            ModelId = modelId,
            MaterialIndex = materialIndex,
            PartialSkeletonIndex = partialSkeletonIndex
        };
        observations.Add(observation);
        resourceManifest.AddObservation(observation);
    }

    private static UeStainSnapshot ToStain(ParsedStain stain)
    {
        return new UeStainSnapshot
        {
            SeColor = stain.SeColor,
            Color = AsFloatArray(stain.Color),
            RowId = stain.RowId,
            Name = stain.Name,
            Shade = stain.Shade,
            SubOrder = stain.SubOrder
        };
    }

    private static UeTextureRuntimeMetadata ToTextureRuntimeMetadata(TextureResource resource)
    {
        return new UeTextureRuntimeMetadata
        {
            Format = resource.Format.ToString(),
            Width = resource.Width,
            Height = resource.Height,
            MipLevels = resource.MipLevels,
            ArraySize = resource.ArraySize,
            Dimension = resource.Dimension.ToString(),
            MiscFlags = resource.MiscFlags.ToString()
        };
    }

    private static UeShapeAttributeSnapshot? ToShapeAttributes(Meddle.Utils.Export.Model.ShapeAttributeGroup? shapeAttributeGroup)
    {
        if (shapeAttributeGroup == null)
        {
            return null;
        }

        return new UeShapeAttributeSnapshot
        {
            EnabledShapeMask = shapeAttributeGroup.EnabledShapeMask,
            EnabledAttributeMask = shapeAttributeGroup.EnabledAttributeMask,
            ShapeMasks = shapeAttributeGroup.ShapeMasks
                                 .OrderBy(x => x.id)
                                 .Select(x => new UeNamedMaskSnapshot { Name = x.name, Id = x.id })
                                 .ToList(),
            AttributeMasks = shapeAttributeGroup.AttributeMasks
                                               .OrderBy(x => x.id)
                                               .Select(x => new UeNamedMaskSnapshot { Name = x.name, Id = x.id })
                                               .ToList()
        };
    }

    private static UeTransformSnapshot? ToTransform(Transform? transform)
    {
        return transform == null ? null : ToTransform(transform.Value);
    }

    private static UeTransformSnapshot ToTransform(Transform transform)
    {
        return new UeTransformSnapshot
        {
            Translation = AsFloatArray(transform.Translation),
            Rotation = AsFloatArray(transform.Rotation),
            Scale = AsFloatArray(transform.Scale)
        };
    }

    private static float[] AsFloatArray(Vector3 value)
    {
        return [value.X, value.Y, value.Z];
    }

    private static float[] AsFloatArray(Vector4 value)
    {
        return [value.X, value.Y, value.Z, value.W];
    }

    private static float[] AsFloatArray(Quaternion value)
    {
        return [value.X, value.Y, value.Z, value.W];
    }

    private static DateTime ToUtc(DateTime value)
    {
        return value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
    }
}
