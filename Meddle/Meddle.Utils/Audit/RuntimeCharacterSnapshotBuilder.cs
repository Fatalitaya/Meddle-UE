namespace Meddle.Utils.Audit;

/// <summary>Projects canonical audit observations into the consumer contract.</summary>
public static class RuntimeCharacterSnapshotBuilder
{
    public static RuntimeCharacterSnapshot Build(RuntimeAuditSession session)
    {
        var observations = session.Observations.Where(o => string.IsNullOrEmpty(o.ActorId) || o.ActorId == session.ActorId).ToArray();
        var gameObject = Value(observations, "GameObject");
        var characterBase = Value(observations, "CharacterBase");
        var human = Value(observations, "Human");
        var skeleton = Value(observations, "Skeleton");
        var equipment = List(observations, "EquipmentVisibility", "slots").OfType<Dictionary<string, object?>>().Select(ToEquipment).ToList();
        var attachments = BuildGraph(session.ActorId, observations);
        var components = List(observations, "Weapon", "weapons").OfType<Dictionary<string, object?>>().Select(w => ToWeapon(w, attachments)).ToList();

        return new RuntimeCharacterSnapshot
        {
            CaptureId = session.CaptureId,
            CapturedAtUtc = session.CapturedAtUtc,
            Actor = new ActorIdentity
            {
                RuntimeId = session.ActorId, Name = session.ActorName, ObjectKind = session.ObjectKind,
                ObjectId = Get(gameObject, "objectId"), DataId = Get(gameObject, "dataId"),
                ModelId = Get(characterBase, "modelId"), ModelType = Get(characterBase, "modelType")
            },
            Customize = BuildCustomize(human),
            Appearance = new AppearanceState
            {
                Equipment = equipment, Components = components,
                Visor = BuildVisor(Value(observations, "VisorState")), Attachments = attachments
            },
            Resources = BuildResources(observations),
            Skeleton = new SkeletonReference
            {
                Available = skeleton != null,
                RuntimeAddress = Get(skeleton, "skeletonAddress"),
                Resources = ListValue(skeleton, "partialSkeletons")
            }
        };
    }

    private static CustomizeState BuildCustomize(Dictionary<string, object?>? human)
    {
        var values = new Dictionary<string, object?>();
        if (human != null)
        {
            if (human.TryGetValue("raceSexId", out var raceSexId)) values["raceSexId"] = raceSexId;
            if (Get(human, "customize") is Dictionary<string, object?> customize)
                foreach (var item in customize) values[item.Key] = item.Value;
            if (human.TryGetValue("parameters", out var parameters)) values["parameterBuffer"] = parameters;
            if (human.TryGetValue("faceDecal", out var faceDecal)) values["faceDecal"] = faceDecal;
        }
        return new CustomizeState { Available = human != null, Values = values };
    }

    private static EquipmentAppearanceState ToEquipment(Dictionary<string, object?> value)
    {
        var equipped = Bool(value, "equipped") == true;
        var visible = Bool(value, "visible");
        var hidden = equipped ? Bool(value, "hidden") : null;
        EquipmentHideReason? reason = !equipped ? null : visible == true ? EquipmentHideReason.Visible :
            hidden == true && Bool(value, "hiddenByRuntimeFlag") == true ? EquipmentHideReason.HiddenByRuntimeFlag :
            EquipmentHideReason.HiddenUnknown;
        return new EquipmentAppearanceState
        {
            Slot = Get(value, "slot"), SlotName = Get(value, "slotName")?.ToString(), Equipped = equipped,
            Visible = equipped ? visible : null, Hidden = hidden,
            RuntimeFlags = Get(value, "runtimeFlags") ?? Get(value, "flags"),
            ModelResource = (Get(value, "modelResource") ?? Get(value, "resource"))?.ToString(),
            HideReason = reason, Source = Get(value, "source")?.ToString() ?? "runtime"
        };
    }

    private static VisorRuntimeState BuildVisor(Dictionary<string, object?>? value)
    {
        var active = Bool(value, "active") ?? Bool(value, "visorToggled");
        return new VisorRuntimeState
        {
            Supported = Bool(value, "supported") ?? Bool(value, "visorSupported"), Active = active,
            State = Get(value, "state")?.ToString() ?? Get(value, "visorState")?.ToString() ?? "unknown",
            AffectedObjects = ListValue(value, "affectedObjects").Select(x => x?.ToString()).Where(x => x != null).Cast<string>().ToList()
        };
    }

    private static CharacterAttachmentGraph BuildGraph(string actorId, IReadOnlyList<RuntimeAuditObservation> observations)
    {
        var graph = new CharacterAttachmentGraph();
        graph.Nodes.Add(new AttachmentNode { Id = actorId, Type = "Actor" });
        foreach (var model in List(observations, "DrawObject", "models").OfType<Dictionary<string, object?>>())
        {
            var id = Get(model, "modelAddress")?.ToString();
            if (string.IsNullOrWhiteSpace(id)) continue;
            graph.Nodes.Add(new AttachmentNode
            {
                Id = id, Type = "AppearanceComponent", Parent = actorId,
                Resource = Get(model, "resource")?.ToString()
            });
            graph.Edges.Add(new AttachmentEdge { Parent = actorId, Child = id, Relation = "appearanceComponent" });
        }
        foreach (var item in List(observations, "Attachment", "attachments").OfType<Dictionary<string, object?>>())
        {
            var parent = Get(item, "parent")?.ToString(); var child = Get(item, "child")?.ToString();
            if (parent == null || child == null) continue;
            var socket = Get(item, "socket") as Dictionary<string, object?>;
            var boneIndex = Int(Get(socket, "boneIndex"));
            var attachment = new AttachmentReference
            {
                BoneIndex = boneIndex,
                BoneNameIfAvailable = Get(socket, "name")?.ToString() ?? (boneIndex.HasValue ? FFXIVBoneSemanticMap.TryGetSemanticName(boneIndex.Value) : null),
                Transform = Get(item, "transform")
            };
            if (graph.Nodes.All(n => n.Id != child)) graph.Nodes.Add(new AttachmentNode { Id = child, Type = "Weapon", Parent = parent });
            graph.Edges.Add(new AttachmentEdge { Parent = parent, Child = child, Relation = "skeletonBone", Attachment = attachment });
        }
        return graph;
    }

    private static AppearanceComponent ToWeapon(Dictionary<string, object?> value, CharacterAttachmentGraph graph)
    {
        var slot = Get(value, "slot")?.ToString() ?? Get(value, "hand")?.ToString() ?? "unknown";
        var drawObject = Get(value, "drawObject")?.ToString();
        var attachment = drawObject == null ? null : graph.Edges.FirstOrDefault(e => e.Child == drawObject)?.Attachment;
        return new AppearanceComponent
        {
            Slot = slot switch { "mainHand" => "MainHand", "offHand" => "OffHand", _ => slot },
            Visible = Bool(value, "visible"), Resource = Get(value, "resource")?.ToString(),
            Model = Get(value, "model"), Materials = Get(value, "materials") ?? Get(value, "material"), Attachment = attachment
        };
    }

    private static List<ResourceBinding> BuildResources(IReadOnlyList<RuntimeAuditObservation> observations)
    {
        var result = new List<ResourceBinding>();
        foreach (var source in new[] { "DrawObject", "Material", "TextureResourceHandle" })
        foreach (var value in FlattenResourceValues(observations, source))
        {
            var logical = (Get(value, "logicalPath") ?? Get(value, "sourcePath"))?.ToString();
            var resolved = (Get(value, "resolvedPath") ?? Get(value, "resource") ?? Get(value, "resourcePath") ?? Get(value, "path"))?.ToString();
            if (logical == null && resolved == null) continue;
            // A differing path is positive evidence of a replacement. Equal
            // paths are not positive evidence of original provenance because
            // a runtime override may deliberately retain the logical name.
            bool? detected = logical != null && resolved != null &&
                             !string.Equals(logical, resolved, StringComparison.OrdinalIgnoreCase)
                ? true
                : null;
            result.Add(new ResourceBinding { LogicalPath = logical, ResolvedPath = resolved, OverrideDetected = detected,
                Provenance = detected == true ? "resolved" : "unknown" });
        }
        return result.DistinctBy(r => (r.LogicalPath, r.ResolvedPath)).ToList();
    }

    private static IEnumerable<Dictionary<string, object?>> FlattenResourceValues(IReadOnlyList<RuntimeAuditObservation> observations, string source)
    {
        foreach (var observation in observations.Where(o => o.SourceType == source))
        {
            if (observation.Value is not Dictionary<string, object?> value) continue;
            var key = source switch { "DrawObject" => "models", "Material" => "materials", _ => "bindings" };
            foreach (var item in ListValue(value, key).OfType<Dictionary<string, object?>>()) yield return item;
            yield return value;
        }
    }

    private static Dictionary<string, object?>? Value(IEnumerable<RuntimeAuditObservation> observations, string source) => observations.LastOrDefault(o => o.SourceType == source)?.Value as Dictionary<string, object?>;
    private static List<object?> List(IEnumerable<RuntimeAuditObservation> observations, string source, string key) => ListValue(Value(observations, source), key);
    private static List<object?> ListValue(Dictionary<string, object?>? value, string key) => value != null && value.TryGetValue(key, out var raw) && raw is System.Collections.IEnumerable list ? list.Cast<object?>().ToList() : [];
    private static object? Get(Dictionary<string, object?>? value, string key) => value != null && value.TryGetValue(key, out var result) ? result : null;
    private static bool? Bool(Dictionary<string, object?>? value, string key) => Get(value, key) is bool result ? result : null;
    private static int? Int(object? value) { try { return value == null ? null : Convert.ToInt32(value); } catch { return null; } }
}
