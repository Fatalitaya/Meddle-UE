namespace Meddle.Utils.Audit;

/// <summary>
/// Builds the consolidated character audit report
/// (<c>FFXIVCharacterRuntimeAudit.json</c>) from a captured session.
///
/// The report reorganizes the canonical observations into the human-readable
/// categories requested for the audit branch: actor, customize, drawObjects,
/// meshes, materials, textures, equipment, attachments, skeleton,
/// penumbraResolution and missingData.
///
/// This is a projection of <see cref="RuntimeAuditObservation"/> values only.
/// Nothing is reconstructed: values keep the FFXIV-native representation the
/// runtime exposed, and anything the runtime did not expose is reported as
/// "unknown" with the reason the collector recorded.
/// </summary>
public static class RuntimeAuditReport
{
    public const string FileName = "FFXIVCharacterRuntimeAudit.json";

    public static Dictionary<string, object?> Build(
        RuntimeAuditSession session,
        DateTime? generatedAtUtc = null)
    {
        var snapshot = RuntimeCharacterSnapshotBuilder.Build(session);
        var observations = session.Observations
                                  .Where(o => string.IsNullOrEmpty(o.ActorId) ||
                                              o.ActorId == session.ActorId)
                                  .ToArray();

        var actor = BuildActor(session, observations);
        var customize = BuildCustomize(observations);
        var modelResources = BuildModelResources(observations);
        var drawObjects = BuildDrawObjects(observations, modelResources);
        var meshes = BuildMeshes(modelResources);
        var materials = BuildMaterials(observations);
        var textures = BuildTextures(observations);
        var equipment = BuildEquipment(observations);
        var attachments = BuildAttachments(observations);
        var skeleton = BuildSkeleton(observations);
        var penumbraResolution = BuildPenumbraResolution(drawObjects);
        var appearanceState = BuildAppearanceState(session, observations, drawObjects, penumbraResolution);
        var missingData = BuildMissingData(observations);

        return new Dictionary<string, object?>
        {
            ["format"] = RuntimeAuditFormat.FormatMarker,
            ["schemaVersion"] = RuntimeAuditFormat.SchemaVersion,
            ["generatedAtUtc"] = (generatedAtUtc ?? DateTime.UtcNow).ToUniversalTime().ToString("O"),
            ["captureId"] = session.CaptureId,
            ["snapshot"] = snapshot,
            ["actor"] = actor,
            ["customize"] = customize,
            ["drawObjects"] = drawObjects,
            ["meshes"] = meshes,
            ["materials"] = materials,
            ["textures"] = textures,
            ["equipment"] = equipment,
            ["attachments"] = attachments,
            ["skeleton"] = skeleton,
            ["penumbraResolution"] = penumbraResolution,
            ["appearanceState"] = appearanceState,
            ["missingData"] = missingData,
            ["verdict"] = BuildVerdict(session, missingData)
        };
    }

    private static Dictionary<string, object?> BuildActor(
        RuntimeAuditSession session,
        IReadOnlyList<RuntimeAuditObservation> observations)
    {
        var identity = new Dictionary<string, object?>
        {
            ["address"] = session.ActorId,
            ["objectKind"] = session.ObjectKind,
            ["name"] = session.ActorName,
            ["objectId"] = null,
            ["dataId"] = null,
            ["modelId"] = null,
            ["race"] = null,
            ["gender"] = null,
            ["tribe"] = null
        };

        foreach (var observation in observations)
        {
            if (observation.Value is not Dictionary<string, object?> value)
            {
                continue;
            }

            if (string.Equals(observation.SourceType, "CharacterBase", StringComparison.Ordinal))
            {
                identity["objectKind"] = value.TryGetValue("objectKind", out var kind)
                    ? kind
                    : session.ObjectKind;
                identity["modelId"] = value.TryGetValue("modelId", out var modelId) ? modelId : null;
                identity["modelType"] = value.TryGetValue("modelType", out var modelType)
                    ? modelType
                    : null;
            }

            if (string.Equals(observation.SourceType, "GameObject", StringComparison.Ordinal))
            {
                identity["objectId"] = value.TryGetValue("objectId", out var objectId) ? objectId : null;
                identity["dataId"] = value.TryGetValue("dataId", out var dataId) ? dataId : null;
            }

            if (string.Equals(observation.SourceType, "Human", StringComparison.Ordinal) &&
                value.TryGetValue("customize", out var customizeRaw) &&
                customizeRaw is Dictionary<string, object?> customize)
            {
                identity["race"] = customize.TryGetValue("race", out var race) ? race : null;
                identity["gender"] = customize.TryGetValue("sex", out var sex) ? sex : null;
                identity["tribe"] = customize.TryGetValue("tribe", out var tribe) ? tribe : null;
            }
        }

        return identity;
    }

    private static Dictionary<string, object?> BuildCustomize(
        IReadOnlyList<RuntimeAuditObservation> observations)
    {
        var observation = observations.FirstOrDefault(
            o => string.Equals(o.SourceType, "Human", StringComparison.Ordinal));
        if (observation == null)
        {
            return new Dictionary<string, object?>
            {
                ["available"] = false,
                ["reason"] = "no customize observation for this target"
            };
        }

        if (observation.Value is not Dictionary<string, object?> value)
        {
            return new Dictionary<string, object?>
            {
                ["available"] = false,
                ["reason"] = observation.Error ?? "customize value unavailable"
            };
        }

        var result = new Dictionary<string, object?>
        {
            ["available"] = observation.Availability == RuntimeAuditAvailability.Available
        };
        if (value.TryGetValue("raceSexId", out var raceSexId))
        {
            result["raceSexId"] = raceSexId;
        }

        if (value.TryGetValue("customize", out var customizeRaw) &&
            customizeRaw is Dictionary<string, object?> customize)
        {
            foreach (var (key, customizeValue) in customize)
            {
                result[key] = customizeValue;
            }
        }

        if (value.TryGetValue("parameters", out var parametersRaw) &&
            parametersRaw is Dictionary<string, object?> parameters)
        {
            result["parameterBuffer"] = parameters;
        }

        if (value.TryGetValue("decalColor", out var decalRaw) &&
            decalRaw is Dictionary<string, object?> decal)
        {
            result["decalColor"] = decal.TryGetValue("color", out var color) ? color : null;
        }

        if (value.TryGetValue("faceDecal", out var faceDecalRaw) &&
            faceDecalRaw is Dictionary<string, object?> faceDecal)
        {
            result["faceDecal"] = faceDecal;
        }

        if (observation.Availability != RuntimeAuditAvailability.Available)
        {
            result["reason"] = observation.Error ?? observation.Availability.ToString();
        }

        return result;
    }

    private static List<Dictionary<string, object?>> BuildModelResources(
        IReadOnlyList<RuntimeAuditObservation> observations)
    {
        var result = new List<Dictionary<string, object?>>();
        foreach (var observation in observations)
        {
            if (string.Equals(observation.SourceType, "ModelResource", StringComparison.Ordinal) &&
                observation.Value is Dictionary<string, object?> value &&
                value.TryGetValue("models", out var modelsRaw) &&
                modelsRaw is System.Collections.IEnumerable models)
            {
                foreach (var model in models.Cast<object?>())
                {
                    if (model is Dictionary<string, object?> modelValue)
                    {
                        result.Add(modelValue);
                    }
                }
            }
        }

        return result;
    }

    private static List<object?> BuildDrawObjects(
        IReadOnlyList<RuntimeAuditObservation> observations,
        IReadOnlyList<Dictionary<string, object?>> modelResources)
    {
        var result = new List<object?>();
        foreach (var observation in observations)
        {
            if (string.Equals(observation.SourceType, "DrawObject", StringComparison.Ordinal) &&
                observation.Value is Dictionary<string, object?> value &&
                value.TryGetValue("models", out var modelsRaw) &&
                modelsRaw is System.Collections.IEnumerable models)
            {
                foreach (var model in models.Cast<object?>())
                {
                    if (model is Dictionary<string, object?> modelValue)
                    {
                        var slotKey = modelValue.TryGetValue("slotIndex", out var slotIndexValue)
                            ? Convert.ToString(slotIndexValue, System.Globalization.CultureInfo.InvariantCulture)
                            : null;
                        var meshEvidence = slotKey == null
                            ? null
                            : modelResources.FirstOrDefault(r =>
                                Equals(
                                    Convert.ToString(
                                        r.TryGetValue("slotIndex", out var resourceSlot)
                                            ? resourceSlot
                                            : null,
                                        System.Globalization.CultureInfo.InvariantCulture),
                                    slotKey));
                        result.Add(new Dictionary<string, object?>
                        {
                            ["slotIndex"] = modelValue.TryGetValue("slotIndex", out var slotIndex)
                                ? slotIndex
                                : null,
                            ["slotName"] = MapModelSlotName(modelValue),
                            ["resource"] = modelValue.TryGetValue("resource", out var resource)
                                ? resource
                                : null,
                            ["logicalPath"] = modelValue.TryGetValue("logicalPath", out var logicalPath)
                                ? logicalPath
                                : null,
                            ["enabled"] = modelValue.TryGetValue("enabled", out var enabled)
                                ? enabled
                                : null,
                            ["variant"] = modelValue.TryGetValue("variant", out var variant)
                                ? variant
                                : null,
                            ["relation"] = modelValue.TryGetValue("relation", out var relation)
                                ? relation
                                : null,
                            ["modelAddress"] = modelValue.TryGetValue("modelAddress", out var modelAddress)
                                ? modelAddress
                                : null,
                            ["meshEvidence"] = meshEvidence
                        });
                    }
                }
            }
        }

        return result;
    }

    private static List<object?> BuildMeshes(
        IReadOnlyList<Dictionary<string, object?>> modelResources)
    {
        var result = new List<object?>();
        foreach (var value in modelResources)
        {
            if (!value.TryGetValue("headerAvailable", out var headerAvailable) || headerAvailable is not true)
            {
                result.Add(new Dictionary<string, object?>
                {
                    ["slotIndex"] = value.TryGetValue("slotIndex", out var unavailableSlot)
                        ? unavailableSlot
                        : null,
                    ["resource"] = value.TryGetValue("resource", out var unavailableResource)
                        ? unavailableResource
                        : null,
                    ["meshDataAvailable"] = false,
                    ["reason"] = value.TryGetValue("reason", out var reason)
                        ? reason
                        : "model header unavailable"
                });
                continue;
            }

            result.Add(new Dictionary<string, object?>
            {
                ["slotIndex"] = value.TryGetValue("slotIndex", out var slotIndex) ? slotIndex : null,
                ["resource"] = value.TryGetValue("resource", out var resource) ? resource : null,
                ["meshDataAvailable"] = value.TryGetValue("meshCount", out var meshCount) &&
                                        meshCount != null &&
                                        Convert.ToInt32(meshCount) > 0,
                ["meshCount"] = value.TryGetValue("meshCount", out var meshCountValue)
                    ? meshCountValue
                    : null,
                ["submeshCount"] = value.TryGetValue("submeshCount", out var submeshCount)
                    ? submeshCount
                    : null,
                ["vertexDeclarationCount"] = value.TryGetValue("vertexDeclarationCount", out var vdCount)
                    ? vdCount
                    : null,
                ["materialCount"] = value.TryGetValue("materialCount", out var materialCount)
                    ? materialCount
                    : null,
                ["boneCount"] = value.TryGetValue("boneCount", out var boneCount)
                    ? boneCount
                    : null,
                ["shapeCount"] = value.TryGetValue("shapeCount", out var shapeCount)
                    ? shapeCount
                    : null,
                ["vertexCount"] = null,
                ["indexCount"] = null,
                ["vertexBufferAvailable"] = null,
                ["normalTangentAvailable"] = null,
                ["uvAvailable"] = null,
                ["reason"] = "per-mesh vertex/index data requires MDL parsing, which is outside the runtime audit scope"
            });
        }

        return result;
    }

    private static List<object?> BuildMaterials(IReadOnlyList<RuntimeAuditObservation> observations)
    {
        var result = new List<object?>();
        foreach (var observation in observations)
        {
            if (string.Equals(observation.SourceType, "Material", StringComparison.Ordinal) &&
                observation.Value is Dictionary<string, object?> value &&
                value.TryGetValue("materials", out var materialsRaw) &&
                materialsRaw is IEnumerable<object?> materials)
            {
                foreach (var material in materials)
                {
                    if (material is Dictionary<string, object?> materialValue)
                    {
                        result.Add(materialValue);
                    }
                }
            }
        }

        return result;
    }

    private static List<object?> BuildTextures(IReadOnlyList<RuntimeAuditObservation> observations)
    {
        var result = new List<object?>();
        foreach (var observation in observations)
        {
            if (string.Equals(observation.SourceType, "TextureResourceHandle", StringComparison.Ordinal) &&
                observation.Value is Dictionary<string, object?> value &&
                value.TryGetValue("bindings", out var bindingsRaw) &&
                bindingsRaw is IEnumerable<object?> bindings)
            {
                foreach (var binding in bindings)
                {
                    if (binding is Dictionary<string, object?> bindingValue)
                    {
                        result.Add(bindingValue);
                    }
                }
            }
        }

        return result;
    }

    private static List<object?> BuildEquipment(IReadOnlyList<RuntimeAuditObservation> observations)
    {
        var result = new List<object?>();
        var dyeBySlot = new Dictionary<string, (object? Stain0, object? Stain1)>();

        foreach (var observation in observations)
        {
            if (observation.Value is not Dictionary<string, object?> value)
            {
                continue;
            }

            if (string.Equals(observation.SourceType, "Human", StringComparison.Ordinal) &&
                value.TryGetValue("equipmentModelIds", out var equipmentRaw) &&
                equipmentRaw is IEnumerable<object?> equipment)
            {
                foreach (var entry in equipment)
                {
                    if (entry is not Dictionary<string, object?> equipmentValue)
                    {
                        continue;
                    }

                    var slotIndex = equipmentValue.TryGetValue("slot", out var slot) ? slot : null;
                    var slotKey = slotIndex == null
                        ? string.Empty
                        : Convert.ToString(slotIndex, System.Globalization.CultureInfo.InvariantCulture);
                    var slotName = equipmentValue.TryGetValue("slotName", out var name) ? name?.ToString() : null;

                    var id = equipmentValue.TryGetValue("id", out var modelId) ? modelId : null;
                    var variant = equipmentValue.TryGetValue("variant", out var variantValue) ? variantValue : null;
                    var stain0 = equipmentValue.TryGetValue("stain0", out var s0) ? s0 : null;
                    var stain1 = equipmentValue.TryGetValue("stain1", out var s1) ? s1 : null;
                    if (slotKey != null && slotKey.Length > 0)
                    {
                        dyeBySlot[slotKey] = (stain0, stain1);
                    }

                    result.Add(new Dictionary<string, object?>
                    {
                        ["slot"] = slotIndex,
                        ["slotName"] = slotName,
                        ["modelId"] = id,
                        ["variant"] = variant,
                        ["finalModel"] = null,
                        ["finalMesh"] = null,
                        ["finalMaterial"] = null,
                        ["stain0"] = stain0,
                        ["stain1"] = stain1,
                        ["resolved"] = false
                    });
                }
            }
        }

        // Match final runtime model/material evidence to equipment slots.
        var drawObjects = BuildDrawObjects(observations, BuildModelResources(observations));
        var materialBySlot = new Dictionary<string, List<object?>>();
        foreach (var material in BuildMaterials(observations))
        {
            if (material is not Dictionary<string, object?> materialValue)
            {
                continue;
            }

            var modelSlot = materialValue.TryGetValue("modelSlot", out var slot) ? slot : null;
            if (modelSlot == null)
            {
                continue;
            }

            var modelSlotKey = Convert.ToString(modelSlot, System.Globalization.CultureInfo.InvariantCulture)!;
            if (!materialBySlot.TryGetValue(modelSlotKey, out var list))
            {
                list = [];
                materialBySlot[modelSlotKey] = list;
            }

            list.Add(materialValue);
        }

        foreach (var drawObject in drawObjects)
        {
            if (drawObject is not Dictionary<string, object?> drawValue ||
                !drawValue.TryGetValue("slotIndex", out var slotIndex))
            {
                continue;
            }

            var slotKey = Convert.ToString(slotIndex, System.Globalization.CultureInfo.InvariantCulture);
            if (slotKey == null)
            {
                continue;
            }

            var entry = slotIndex == null
                ? null
                : result.OfType<Dictionary<string, object?>>()
                        .FirstOrDefault(e =>
                            Equals(
                                Convert.ToString(e["slot"], System.Globalization.CultureInfo.InvariantCulture),
                                slotKey));
            if (entry == null)
            {
                continue;
            }

            if (entry["slotName"] == null && drawValue.TryGetValue("slotName", out var slotName))
            {
                entry["slotName"] = slotName;
            }
            entry["finalModel"] = drawValue.TryGetValue("resource", out var resource) ? resource : null;
            entry["finalMesh"] = drawValue.TryGetValue("meshEvidence", out var meshEvidence)
                ? meshEvidence
                : null;
            entry["finalMaterial"] = materialBySlot.TryGetValue(slotKey, out var materials)
                ? materials
                : null;
            entry["resolved"] = entry["finalModel"] != null;

            if (dyeBySlot.TryGetValue(slotKey, out var dye))
            {
                entry["stain0"] = dye.Stain0;
                entry["stain1"] = dye.Stain1;
            }
        }

        return result;
    }

    private static List<object?> BuildAttachments(IReadOnlyList<RuntimeAuditObservation> observations)
    {
        var result = new List<object?>();
        foreach (var observation in observations)
        {
            if (observation.Value is not Dictionary<string, object?> value)
            {
                continue;
            }

            if (string.Equals(observation.SourceType, "DrawObject", StringComparison.Ordinal) &&
                value.TryGetValue("attach", out var attachRaw))
            {
                result.Add(new Dictionary<string, object?>
                {
                    ["source"] = "CharacterBase.Attach",
                    ["attachmentData"] = attachRaw
                });
            }

            if (string.Equals(observation.SourceType, "Skeleton", StringComparison.Ordinal) &&
                value.TryGetValue("attachRelationships", out var relationshipsRaw))
            {
                result.Add(new Dictionary<string, object?>
                {
                    ["source"] = "Skeleton.AttachRelationships",
                    ["attachmentData"] = relationshipsRaw
                });
            }
        }

        return result;
    }

    private static Dictionary<string, object?> BuildSkeleton(
        IReadOnlyList<RuntimeAuditObservation> observations)
    {
        var observation = observations.FirstOrDefault(
            o => string.Equals(o.SourceType, "Skeleton", StringComparison.Ordinal));
        if (observation == null ||
            observation.Value is not Dictionary<string, object?> value)
        {
            return new Dictionary<string, object?>
            {
                ["available"] = false,
                ["completeness"] = "UNKNOWN",
                ["reason"] = observation?.Error ?? "no skeleton observation for this target"
            };
        }

        var result = new Dictionary<string, object?>
        {
            ["available"] = observation.Availability == RuntimeAuditAvailability.Available,
            ["partialSkeletonCount"] = value.TryGetValue("partialSkeletonCount", out var count)
                ? count
                : null,
            ["skeletonAddress"] = value.TryGetValue("skeletonAddress", out var address)
                ? address
                : null,
            ["partialSkeletons"] = value.TryGetValue("partialSkeletons", out var partials)
                ? NormalizeList(partials)
                : null
        };

        var complete = false;
        var partial = false;
        if (value.TryGetValue("partialSkeletons", out var partialsRaw) &&
            partialsRaw is IEnumerable<object?> partialSkeletons)
        {
            foreach (var partialSkeleton in partialSkeletons)
            {
                if (partialSkeleton is not Dictionary<string, object?> partialValue)
                {
                    continue;
                }

                var handleAvailable = partialValue.TryGetValue("resourceIdentityAvailable", out var ra) &&
                                      ra is true;
                var boneNames = partialValue.TryGetValue("boneNames", out var names) &&
                                names is IEnumerable<object?> boneNameList &&
                                boneNameList.Any();
                var hierarchy = partialValue.TryGetValue("hierarchyAvailable", out var ha) &&
                                ha is true;
                if (handleAvailable && boneNames && hierarchy)
                {
                    complete = true;
                }
                else if (handleAvailable || boneNames)
                {
                    partial = true;
                }
            }
        }

        result["completeness"] = complete ? "COMPLETE" : partial ? "PARTIAL" : "UNKNOWN";
        return result;
    }

    private static List<object?> NormalizeList(object? value)
    {
        return value switch
        {
            null => [],
            IEnumerable<object?> enumerable => enumerable.ToList(),
            _ => [value]
        };
    }

    private static Dictionary<string, object?> BuildPenumbraResolution(
        IReadOnlyList<object?> drawObjects)
    {
        var resources = new List<object?>();
        var overrideObserved = false;
        foreach (var drawObject in drawObjects)
        {
            if (drawObject is not Dictionary<string, object?> drawValue)
            {
                continue;
            }

            var resource = drawValue.TryGetValue("resource", out var resourceValue)
                ? resourceValue?.ToString()
                : null;
            var logicalPath = drawValue.TryGetValue("logicalPath", out var logicalValue)
                ? logicalValue?.ToString()
                : null;
            if (string.IsNullOrWhiteSpace(resource))
            {
                continue;
            }

            var pathsDiffer = !string.IsNullOrWhiteSpace(logicalPath) &&
                              !string.Equals(resource, logicalPath, StringComparison.OrdinalIgnoreCase);
            bool? overridden = pathsDiffer ? true : null;
            overrideObserved |= pathsDiffer;
            resources.Add(new Dictionary<string, object?>
            {
                ["slotIndex"] = drawValue.TryGetValue("slotIndex", out var slotIndex)
                    ? slotIndex
                    : null,
                ["original"] = logicalPath,
                ["resolved"] = resource,
                ["resolvedOnly"] = string.IsNullOrWhiteSpace(logicalPath),
                ["overridden"] = overridden,
                ["overrideObserved"] = pathsDiffer,
                ["provenance"] = pathsDiffer ? "resolved" : "unknown",
                ["note"] = pathsDiffer
                    ? "runtime resource handle path differs from the logical path; this is consistent with a Penumbra-style resource replacement"
                    : "equal or missing paths do not independently prove original-file provenance"
            });
        }

        return new Dictionary<string, object?>
        {
            ["observed"] = resources.Count > 0,
            ["overrideObserved"] = overrideObserved,
            ["resources"] = resources,
            ["limitation"] = "The audit only observes final runtime paths. It cannot enumerate Penumbra's mod state; "
                             + "a resource replaced at load time is indistinguishable from a game file with the same name."
        };
    }

    private static Dictionary<string, object?> BuildAppearanceState(
        RuntimeAuditSession session,
        IReadOnlyList<RuntimeAuditObservation> observations,
        IReadOnlyList<object?> drawObjects,
        Dictionary<string, object?> resources)
    {
        var equipmentVisibility = ObservationList(observations, "EquipmentVisibility", "slots");
        var visorObservation = ObservationValue(observations, "VisorState");
        var visor = visorObservation ?? new Dictionary<string, object?>
        {
            ["visorSupported"] = null,
            ["visorState"] = null,
            ["affectedSlots"] = Array.Empty<object?>(),
            ["reason"] = "no runtime visor observation"
        };
        var attachments = ObservationList(observations, "Attachment", "attachments");
        var weapons = ObservationList(observations, "Weapon", "weapons");

        var hierarchy = new List<object?>
        {
            new Dictionary<string, object?>
            {
                ["nodeId"] = session.ActorId,
                ["parentId"] = null,
                ["type"] = "Actor",
                ["slot"] = null,
                ["resource"] = null
            }
        };
        foreach (var drawObject in drawObjects.OfType<Dictionary<string, object?>>())
        {
            var address = drawObject.TryGetValue("modelAddress", out var modelAddress) ? modelAddress : null;
            hierarchy.Add(new Dictionary<string, object?>
            {
                ["nodeId"] = address,
                ["parentId"] = session.ActorId,
                ["type"] = "Model",
                ["slot"] = drawObject.TryGetValue("slotName", out var slotName) && slotName != null
                    ? slotName
                    : drawObject.TryGetValue("slotIndex", out var slotIndex) ? slotIndex : null,
                ["resource"] = drawObject.TryGetValue("resource", out var resource) ? resource : null
            });
        }
        foreach (var weapon in weapons.OfType<Dictionary<string, object?>>())
        {
            hierarchy.Add(new Dictionary<string, object?>
            {
                ["nodeId"] = weapon.TryGetValue("drawObject", out var address) ? address : null,
                ["parentId"] = session.ActorId,
                ["type"] = "Weapon",
                ["slot"] = weapon.TryGetValue("hand", out var hand) ? hand : null,
                ["resource"] = weapon.TryGetValue("resource", out var resource) ? resource : null
            });
        }

        return new Dictionary<string, object?>
        {
            ["equipmentVisibility"] = equipmentVisibility,
            ["visor"] = visor,
            ["hierarchy"] = hierarchy,
            ["attachments"] = attachments,
            ["weapons"] = weapons,
            ["resources"] = resources
        };
    }

    private static Dictionary<string, object?>? ObservationValue(
        IReadOnlyList<RuntimeAuditObservation> observations,
        string sourceType) => observations
            .LastOrDefault(o => string.Equals(o.SourceType, sourceType, StringComparison.Ordinal))
            ?.Value as Dictionary<string, object?>;

    private static List<object?> ObservationList(
        IReadOnlyList<RuntimeAuditObservation> observations,
        string sourceType,
        string key)
    {
        var value = ObservationValue(observations, sourceType);
        return value != null && value.TryGetValue(key, out var raw) ? NormalizeList(raw) : [];
    }

    private static List<object?> BuildMissingData(IReadOnlyList<RuntimeAuditObservation> observations)
    {
        var result = new List<object?>();
        foreach (var observation in observations)
        {
            if (observation.Availability == RuntimeAuditAvailability.Available)
            {
                continue;
            }

            result.Add(new Dictionary<string, object?>
            {
                ["category"] = observation.Category,
                ["sourceType"] = observation.SourceType,
                ["availability"] = observation.Availability.ToString(),
                ["reason"] = observation.Error ?? "no reason recorded"
            });
        }

        // Known structural gaps of the current audit instrument. These are
        // runtime observation limitations, not missing data inside the game.
        if (observations.All(o => !string.Equals(o.SourceType, "ModelResource", StringComparison.Ordinal)))
        {
            result.Add(new Dictionary<string, object?>
            {
                ["category"] = "Mesh",
                ["sourceType"] = "ModelResource",
                ["availability"] = "Unknown",
                ["reason"] = "per-mesh vertex/index/buffer data requires MDL parsing, which is outside the runtime audit scope"
            });
        }

        if (observations.All(o => !string.Equals(o.SourceType, "Weapon", StringComparison.Ordinal)))
        {
            result.Add(new Dictionary<string, object?>
            {
                ["category"] = "Equipment",
                ["sourceType"] = "Weapon",
                ["availability"] = "Unknown",
                ["reason"] = "main-hand/off-hand weapon draw objects are not captured as separate composition observations"
            });
        }

        foreach (var sourceType in new[] { "Mount", "Ornament" })
        {
            if (observations.All(o => !string.Equals(o.SourceType, sourceType, StringComparison.Ordinal)))
            {
                result.Add(new Dictionary<string, object?>
                {
                    ["category"] = "Attachment",
                    ["sourceType"] = sourceType,
                    ["availability"] = "Unknown",
                    ["reason"] = $"{sourceType.ToLowerInvariant()} draw state is not independently captured by the current experimental audit"
                });
            }
        }

        return result;
    }

    private static Dictionary<string, object?> BuildVerdict(
        RuntimeAuditSession session,
        IReadOnlyList<object?> missingData)
    {
        var categories = new Dictionary<string, object?>();
        foreach (var (category, status) in session.CollectorStatus)
        {
            categories[category] = status.ToString();
        }

        var gaps = missingData
                   .OfType<Dictionary<string, object?>>()
                   .Select(entry =>
                       $"{(entry.TryGetValue("category", out var category) ? category : "?")}:" +
                       $"{(entry.TryGetValue("sourceType", out var source) ? source : "?")}")
                   .Distinct()
                   .ToArray();

        var coreCategories = new[]
        {
            RuntimeAuditCategory.CharacterComposition.ToString(),
            RuntimeAuditCategory.SkeletonEvidence.ToString(),
            RuntimeAuditCategory.Material.ToString(),
            RuntimeAuditCategory.RuntimeTexture.ToString(),
            RuntimeAuditCategory.Customize.ToString()
        };
        var missingCore = coreCategories
                          .Where(category => session.CollectorStatus.TryGetValue(category, out var status) &&
                                             status != RuntimeAuditAvailability.Available)
                          .ToArray();

        var hasKnownCompositionGap = gaps.Any(gap =>
            gap.EndsWith(":Mount", StringComparison.Ordinal) ||
            gap.EndsWith(":Ornament", StringComparison.Ordinal));

        return new Dictionary<string, object?>
        {
            ["reconstructionReady"] = missingCore.Length == 0 && !hasKnownCompositionGap,
            ["missingCoreCategories"] = missingCore,
            ["gaps"] = gaps,
            ["note"] = "A reconstruction-ready verdict only means the runtime exposed the audited evidence. "
                       + "FFXIVImporter still needs MDL/MTRL/SKLB/PAP parsing and its own reconstruction logic."
        };
    }

    private static string? MapModelSlotName(Dictionary<string, object?> modelValue)
    {
        // Model.SlotIndex is retained as raw evidence. No stable, cited
        // client-struct enum currently proves the appearance semantics here.
        return null;
    }
}
