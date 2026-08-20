using System.Text.Json;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using Meddle.Plugin.Models;
using Meddle.Plugin.Models.Layout;
using Meddle.Plugin.Models.Skeletons;
using Meddle.Plugin.Utils;
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

    private sealed record UePathResourceResult(UeResourceReference? Reference, UeResourceReadStatus Status);

    public UeSnapshotBuilder(
        IUeResourceReader resourceReader,
        CancellationToken cancellationToken)
    {
        this.resourceReader = resourceReader;
        this.cancellationToken = cancellationToken;
    }

    public IReadOnlyList<UeExportObservation> Observations => observations;

    public UeCharacterSnapshot? Build(ParsedCharacterInfo root, string? displayName, string? objectKind)
    {
        return BuildCharacter(root, isRoot: true, displayName, objectKind, new HashSet<ParsedCharacterInfo>(ReferenceEqualityComparer.Instance));
    }

    public UeResourceBundle BuildBundle()
    {
        return resourceManifest.BuildBundle();
    }

    private UeCharacterSnapshot? BuildCharacter(
        ParsedCharacterInfo info,
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
            var snapshot = new UeCharacterSnapshot();
            if (isRoot)
            {
                var provenance = BuildProvenance.Current;
                snapshot.Generator = new UeGeneratorSnapshot
                {
                    Version = provenance.Version,
                    Commit = provenance.Commit,
                    Dirty = provenance.Dirty,
                    BuildId = provenance.BuildId
                };
            }
            else
            {
                snapshot.Generator = null;
                snapshot.KnownCaptureGaps = [];
            }

            snapshot.Capture = new UeCaptureSnapshot
            {
                CapturedAtUtc = ToUtc(info.ParsedAt),
                CharacterName = isRoot ? displayName : null,
                ObjectKind = isRoot ? objectKind : null,
                ModelType = info.ModelType.ToString(),
                GenderRace = (int)info.GenderRace
            };
            snapshot.Customize = BuildCustomize(info);

            snapshot.Skeleton = BuildSkeleton(info.Skeleton);
            snapshot.Attach = BuildAttach(info.Attach);

            for (var modelIndex = 0; modelIndex < info.Models.Count; modelIndex++)
            {
                snapshot.Models.Add(BuildModel(info.Models[modelIndex], modelIndex));
            }

            foreach (var attach in info.Attaches)
            {
                var child = BuildCharacter(attach, isRoot: false, null, null, activePath);
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

    private UeModelSnapshot BuildModel(ParsedModelInfo model, int modelIndex)
    {
        var modelId = $"model-{modelIndex}";
        var modelReference = RegisterPathResource(model.Path.GamePath, model.Path.FullPath, UeResourceType.Mdl, modelId);
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
            Deformation = ToDeformation(model, modelId)
        };

        for (var materialIndex = 0; materialIndex < model.Materials.Length; materialIndex++)
        {
            var material = model.Materials[materialIndex];
            snapshot.Materials.Add(
                material == null
                    ? null
                    : BuildMaterial(material, modelId, materialIndex));
        }

        return snapshot;
    }

    private UeMaterialSnapshot? BuildMaterial(ParsedMaterialInfo material, string modelId, int materialIndex)
    {
        var materialReference = RegisterPathResource(material.Path.GamePath, material.Path.FullPath, UeResourceType.Mtrl, modelId, materialIndex);
        var shpkPath = string.IsNullOrWhiteSpace(material.Shpk)
            ? null
            : $"shader/sm5/shpk/{material.Shpk}";
        var shpkReference = string.IsNullOrWhiteSpace(shpkPath)
            ? null
            : RegisterPathResource(shpkPath, shpkPath, UeResourceType.Shpk, modelId, materialIndex);

        JsonElement? colorTable = null;
        if (material.ColorTableBlob != null)
        {
            colorTable = JsonSerializer.SerializeToElement(material.ColorTableBlob, UeExportJson.Options);
        }

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
            ColorTable = colorTable,
            RenderOverrides = material.RenderMaterialOutput == null
                ? null
                : BuildRenderOverrides(material.RenderMaterialOutput, modelId, materialIndex)
        };

        for (var textureIndex = 0; textureIndex < material.Textures.Length; textureIndex++)
        {
            var texture = material.Textures[textureIndex];
            var textureReference = RegisterPathResource(texture.Path.GamePath, texture.Path.FullPath, UeResourceType.Tex, modelId, materialIndex);
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
                recordFailure: !hasRuntimeFallback);
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
                    materialIndex);
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
            var reference = RegisterPathResource(logicalPath, resolvedPath, UeResourceType.Tex, modelId, materialIndex);
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

    private UeDeformerSnapshot ToDeformation(ParsedModelInfo model, string modelId)
    {
        if (model.ModelType != null && model.ModelType != CharacterBase.ModelType.Human)
        {
            return new UeDeformerSnapshot
            {
                Status = "NotApplicable"
            };
        }

        if (model.Deformer == null)
        {
            AddObservationEvent(
                "DeformerNotObserved",
                "Deformer state was not observed for this model.",
                modelId: modelId);
            return new UeDeformerSnapshot
            {
                Status = "NotObserved"
            };
        }

        var deformer = model.Deformer.Value;
        var pbdReference = string.IsNullOrWhiteSpace(deformer.PbdPath)
            ? null
            : RegisterPathResource(deformer.PbdPath, deformer.PbdPath, UeResourceType.Pbd, modelId);
        if (pbdReference == null)
        {
            AddObservationEvent(
                "DeformerPbdSourceNotObserved",
                "Deformer was observed but no PBD source path was observed.",
                modelId: modelId);
        }

        return new UeDeformerSnapshot
        {
            Status = "Observed",
            PbdResourceId = pbdReference?.Id,
            RaceSexId = deformer.RaceSexId,
            DeformerId = deformer.DeformerId
        };
    }

    private UeSkeletonSnapshot BuildSkeleton(ParsedSkeleton skeleton)
    {
        var snapshot = new UeSkeletonSnapshot
        {
            Transform = ToTransform(skeleton.Transform)
        };

        for (var i = 0; i < skeleton.PartialSkeletons.Count; i++)
        {
            snapshot.PartialSkeletons.Add(BuildPartialSkeleton(skeleton.PartialSkeletons[i], i));
        }

        return snapshot;
    }

    private UePartialSkeletonSnapshot BuildPartialSkeleton(ParsedPartialSkeleton partial, int index)
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
                        partialSkeletonIndex: index);
                }
            }
            else
            {
                var reference = RegisterPathResource(
                    partial.HandlePath,
                    partial.HandlePath,
                    UeResourceType.Sklb,
                    partialSkeletonIndex: index);
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

    private UeAttachSnapshot BuildAttach(ParsedAttach attach)
    {
        var snapshot = new UeAttachSnapshot
        {
            AttachmentCount = attach.AttachmentCount,
            ExecuteType = attach.ExecuteType,
            OffsetTransform = attach.OffsetTransform == null ? null : ToTransform(attach.OffsetTransform),
            PartialSkeletonIdx = attach.PartialSkeletonIdx,
            BoneIdx = attach.BoneIdx
        };

        snapshot.OwnerSkeleton = attach.OwnerSkeleton == null ? null : BuildSkeleton(attach.OwnerSkeleton);
        snapshot.TargetSkeleton = attach.TargetSkeleton == null ? null : BuildSkeleton(attach.TargetSkeleton);
        return snapshot;
    }

    private UeResourceReference? RegisterPathResource(
        string? logicalPath,
        string? resolvedPath,
        UeResourceType fallbackType,
        string? modelId = null,
        int? materialIndex = null,
        int? partialSkeletonIndex = null,
        bool recordFailure = true)
    {
        return ReadPathResource(
            logicalPath,
            resolvedPath,
            fallbackType,
            modelId,
            materialIndex,
            partialSkeletonIndex,
            recordFailure).Reference;
    }

    private UePathResourceResult ReadPathResource(
        string? logicalPath,
        string? resolvedPath,
        UeResourceType fallbackType,
        string? modelId = null,
        int? materialIndex = null,
        int? partialSkeletonIndex = null,
        bool recordFailure = true)
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
                    partialSkeletonIndex: partialSkeletonIndex);
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
                        partialSkeletonIndex);
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
                        partialSkeletonIndex);
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
        return new UePathResourceResult(reference, UeResourceReadStatus.Success);
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
        int? partialSkeletonIndex = null)
    {
        var observation = new UeExportObservation
        {
            Code = code,
            Message = message,
            ResourceType = resourceType?.ToString().ToLowerInvariant(),
            LogicalPath = logicalPath,
            ResolvedPath = resolvedPath,
            ResourceId = resourceId,
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

    private static float[] AsFloatArray(System.Numerics.Vector3 value)
    {
        return [value.X, value.Y, value.Z];
    }

    private static float[] AsFloatArray(System.Numerics.Vector4 value)
    {
        return [value.X, value.Y, value.Z, value.W];
    }

    private static float[] AsFloatArray(System.Numerics.Quaternion value)
    {
        return [value.X, value.Y, value.Z, value.W];
    }

    private static DateTime ToUtc(DateTime value)
    {
        return value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
    }
}
