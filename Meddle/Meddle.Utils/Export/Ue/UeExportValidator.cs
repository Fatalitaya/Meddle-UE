using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Meddle.Utils.Export.Ue;

public sealed class UeExportException : Exception
{
    public UeExportException(string message, IReadOnlyList<UeExportObservation> observations, Exception? innerException = null)
        : base(message, innerException)
    {
        Observations = observations;
    }

    public IReadOnlyList<UeExportObservation> Observations { get; }
}

public static class UeExportValidator
{
    private static readonly Regex CanonicalSha256Pattern = new("^[0-9a-f]{64}$", RegexOptions.Compiled);

    public static void Validate(UeCharacterSnapshot snapshot, UeResourceManifest manifest, string outputDirectory)
    {
        if (snapshot == null)
        {
            throw new UeExportException("Snapshot is null.", []);
        }

        if (snapshot.SchemaVersion != UeExportSchema.CurrentVersion)
        {
            throw new UeExportException($"Unsupported snapshot schema version {snapshot.SchemaVersion}; current version is {UeExportSchema.CurrentVersion}.", []);
        }

        if (manifest == null)
        {
            throw new UeExportException("Manifest is null.", []);
        }

        if (manifest.SchemaVersion != snapshot.SchemaVersion)
        {
            throw new UeExportException(
                $"Manifest schema version {manifest.SchemaVersion} does not match snapshot schema version {snapshot.SchemaVersion}.",
                []);
        }

        if (manifest.SchemaVersion != UeExportSchema.CurrentVersion)
        {
            throw new UeExportException($"Unsupported manifest schema version {manifest.SchemaVersion}; current version is {UeExportSchema.CurrentVersion}.", []);
        }

        if (snapshot.Root == null)
        {
            throw new UeExportException("Snapshot has no root node.", []);
        }

        AssertNoPointerProperties(snapshot.GetType());
        AssertNoPointerProperties(manifest.GetType());

        var json = UeExportJson.Serialize(snapshot);
        if (json.Contains("ModelAddress", StringComparison.Ordinal) ||
            json.Contains("IntPtr", StringComparison.Ordinal) ||
            json.Contains("nint", StringComparison.Ordinal) ||
            json.Contains("\"header\"", StringComparison.Ordinal))
        {
            throw new UeExportException("Snapshot serialized output contains unstable or obsolete ABI metadata.", []);
        }

        var manifestJson = UeExportJson.Serialize(manifest);
        if (manifestJson.Contains("ModelAddress", StringComparison.Ordinal) ||
            manifestJson.Contains("IntPtr", StringComparison.Ordinal) ||
            manifestJson.Contains("nint", StringComparison.Ordinal) ||
            manifestJson.Contains("\"header\"", StringComparison.Ordinal))
        {
            throw new UeExportException("Manifest serialized output contains unstable or obsolete ABI metadata.", []);
        }

        ValidateDeformerStates(snapshot.Root);

        var duplicateIds = manifest.Resources
                                   .GroupBy(x => x.Id, StringComparer.Ordinal)
                                   .Where(g => g.Count() > 1)
                                   .Select(g => g.Key)
                                   .ToArray();
        if (duplicateIds.Length > 0)
        {
            throw new UeExportException(
                $"Resource ids are not unique: {string.Join(", ", duplicateIds)}.",
                []);
        }

        ValidateSharedPhysicalFiles(manifest);

        var referencedIds = CollectReferencedResourceIds(snapshot);
        var manifestById = manifest.Resources.ToDictionary(x => x.Id, StringComparer.Ordinal);
        foreach (var resourceId in referencedIds)
        {
            if (!manifestById.TryGetValue(resourceId, out var entry))
            {
                throw new UeExportException(
                    $"Snapshot references resource id '{resourceId}' that is missing from the manifest.",
                    []);
            }

            ValidateEntry(entry, outputDirectory);
        }

        foreach (var entry in manifest.Resources)
        {
            ValidateEntry(entry, outputDirectory);
        }

        var identities = CollectNodeAndModelIdentities(snapshot.Root);
        foreach (var observation in manifest.Observations)
        {
            ValidateObservation(observation, manifestById, identities);
        }
    }

    public static IReadOnlyCollection<string> CollectReferencedResourceIds(UeCharacterSnapshot snapshot)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        if (snapshot?.Root != null)
        {
            AddNode(snapshot.Root);
        }

        return ids;

        void AddNode(UeCharacterNodeSnapshot node)
        {
            foreach (var model in node.Models)
            {
                Add(model.ResourceId);
                foreach (var material in model.Materials)
                {
                    if (material == null)
                    {
                        continue;
                    }

                    Add(material.ResourceId);
                    Add(material.ShaderPackageResourceId);
                    foreach (var texture in material.Textures)
                    {
                        Add(texture.ResourceId);
                    }

                    if (material.RenderOverrides != null)
                    {
                        Add(material.RenderOverrides.DecalTextureResourceId);
                        foreach (var skinTexture in material.RenderOverrides.SkinMaterialTextures)
                        {
                            Add(skinTexture.ResourceId);
                        }
                    }
                }

                if (model.Deformation != null)
                {
                    if (model.Deformation.PbdResource != null)
                    {
                        Add(model.Deformation.PbdResource.ResourceId);
                    }
                }
            }

            if (node.Skeleton != null)
            {
                AddSkeleton(node.Skeleton);
            }

            if (node.Attach != null)
            {
                if (node.Attach.OwnerSkeleton != null)
                {
                    AddSkeleton(node.Attach.OwnerSkeleton);
                }

                if (node.Attach.TargetSkeleton != null)
                {
                    AddSkeleton(node.Attach.TargetSkeleton);
                }
            }

            foreach (var child in node.Attaches)
            {
                AddNode(child);
            }
        }

        void Add(string? resourceId)
        {
            if (!string.IsNullOrWhiteSpace(resourceId))
            {
                ids.Add(resourceId);
            }
        }

        void AddSkeleton(UeSkeletonSnapshot skeleton)
        {
            foreach (var partial in skeleton.PartialSkeletons)
            {
                Add(partial.ResourceId);
            }
        }
    }

    private static NodeModelIdentities CollectNodeAndModelIdentities(UeCharacterNodeSnapshot root)
    {
        var nodeIds = new HashSet<string>(StringComparer.Ordinal);
        var modelIds = new HashSet<string>(StringComparer.Ordinal);
        AddNode(root);
        return new NodeModelIdentities(nodeIds, modelIds);

        void AddNode(UeCharacterNodeSnapshot node)
        {
            if (string.IsNullOrWhiteSpace(node.NodeId))
            {
                throw new UeExportException("Snapshot contains a node with no deterministic nodeId.", []);
            }

            if (!nodeIds.Add(node.NodeId))
            {
                throw new UeExportException($"Snapshot contains duplicate nodeId '{node.NodeId}'.", []);
            }

            foreach (var model in node.Models)
            {
                if (string.IsNullOrWhiteSpace(model.Id))
                {
                    throw new UeExportException($"Node '{node.NodeId}' contains a model with no id.", []);
                }

                if (!modelIds.Add(model.Id))
                {
                    throw new UeExportException($"Snapshot contains duplicate modelId '{model.Id}'.", []);
                }
            }

            foreach (var child in node.Attaches)
            {
                AddNode(child);
            }
        }
    }

    private static void ValidateDeformerStates(UeCharacterNodeSnapshot root)
    {
        ValidateNode(root);
        return;

        void ValidateNode(UeCharacterNodeSnapshot node)
        {
            foreach (var model in node.Models)
            {
                if (model.Deformation == null)
                {
                    continue;
                }

                ValidateDeformer(model.Deformation, node.NodeId, model.Id);
            }

            foreach (var child in node.Attaches)
            {
                ValidateNode(child);
            }
        }
    }

    private static void ValidateDeformer(
        UeDeformerSnapshot deformer,
        string nodeId,
        string modelId)
    {
        if (!IsAllowedDeformerStatus(deformer.Status))
        {
            throw new UeExportException(
                $"Model '{modelId}' in node '{nodeId}' has obsolete or unsupported deformer status '{deformer.Status}'.",
                []);
        }

        if (deformer.Status != UeDeformerStatus.Observed)
        {
            if (deformer.PbdResource != null ||
                !string.IsNullOrWhiteSpace(deformer.PbdResourcePath))
            {
                throw new UeExportException(
                    $"Model '{modelId}' has deformer status '{deformer.Status}' but also declares PBD resource data.",
                    []);
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(deformer.PbdResourcePath))
        {
            throw new UeExportException(
                $"Model '{modelId}' has observed deformer state but no pbdResourcePath.",
                []);
        }

        if (deformer.PbdResource == null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(deformer.PbdResource.ResourceId))
        {
            throw new UeExportException(
                $"Model '{modelId}' has observed deformer pbdResource but no resourceId.",
                []);
        }

        if (!Enum.IsDefined(deformer.PbdResource.Availability))
        {
            throw new UeExportException(
                $"Model '{modelId}' has invalid pbdResource availability '{deformer.PbdResource.Availability}'.",
                []);
        }
    }

    private static bool IsAllowedDeformerStatus(string status)
    {
        return status switch
        {
            UeDeformerStatus.Observed => true,
            UeDeformerStatus.ObservedNone => true,
            UeDeformerStatus.ObservedIncomplete => true,
            UeDeformerStatus.NotObserved => true,
            UeDeformerStatus.NotApplicable => true,
            _ => false
        };
    }

    private static void ValidateEntry(UeResourceEntry entry, string outputDirectory)
    {
        if (string.IsNullOrWhiteSpace(entry.Id))
        {
            throw new UeExportException("Manifest contains a resource entry with no id.", []);
        }

        ValidateOrigin(entry);

        var type = ParseKnownResourceType(entry.Type);

        switch (entry.Availability)
        {
            case UeResourceAvailability.Available:
                ValidateAvailableEntry(entry, type, outputDirectory);
                break;
            case UeResourceAvailability.Unavailable:
            case UeResourceAvailability.Unresolved:
                ValidateObservationEntry(entry, type);
                break;
            default:
                throw new UeExportException(
                    $"Resource '{entry.Id}' has invalid availability '{entry.Availability}'.",
                    []);
        }
    }

    private static void ValidateAvailableEntry(
        UeResourceEntry entry,
        UeResourceType type,
        string outputDirectory)
    {
        if (string.IsNullOrWhiteSpace(entry.File) ||
            string.IsNullOrWhiteSpace(entry.Sha256))
        {
            throw new UeExportException(
                $"Resource '{entry.Id}' is declared available but is missing file or sha256.",
                []);
        }

        if (!CanonicalSha256Pattern.IsMatch(entry.Sha256))
        {
            throw new UeExportException(
                $"Resource '{entry.Id}' has non-canonical sha256 '{entry.Sha256}'.",
                []);
        }

        if (entry.RuntimeTexture != null && type != UeResourceType.Tex)
        {
            throw new UeExportException(
                $"Resource '{entry.Id}' has runtime texture metadata but type '{entry.Type}' is not tex.",
                []);
        }

        if (!entry.File.StartsWith("resources/", StringComparison.Ordinal) ||
            entry.File.IndexOf('/', "resources/".Length) >= 0)
        {
            throw new UeExportException($"Resource '{entry.Id}' is not stored in a flat resources directory.", []);
        }

        var runtimeEncoding = entry.RuntimeTexture?.Encoding;
        if (entry.RuntimeTexture != null && string.IsNullOrWhiteSpace(runtimeEncoding))
        {
            throw new UeExportException(
                $"Resource '{entry.Id}' has runtime texture metadata with no encoding.",
                []);
        }

        var expectedId = UeResourceIdentity.CreateResourceId(entry.Sha256, type, entry.RuntimeTexture);
        if (!string.Equals(entry.Id, expectedId, StringComparison.Ordinal))
        {
            throw new UeExportException(
                $"Resource '{entry.Id}' content id does not match expected '{expectedId}'.",
                []);
        }

        var filePath = Path.Combine(outputDirectory, entry.File);
        if (!File.Exists(filePath))
        {
            throw new UeExportException($"Resource file '{entry.File}' does not exist.", []);
        }

        var bytes = File.ReadAllBytes(filePath);
        var sha256 = UeResourceIdentity.GetSha256(bytes);
        if (!string.Equals(sha256, entry.Sha256, StringComparison.Ordinal))
        {
            throw new UeExportException(
                $"Resource '{entry.Id}' SHA-256 mismatch: manifest says {entry.Sha256}, disk has {sha256}.",
                []);
        }
    }

    private static void ValidateObservationEntry(UeResourceEntry entry, UeResourceType type)
    {
        if (entry.File != null || entry.Sha256 != null || entry.Size != null)
        {
            throw new UeExportException(
                $"Resource '{entry.Id}' is '{entry.Availability}' but declares file, sha256, or size.",
                []);
        }

        if (entry.RuntimeTexture != null)
        {
            throw new UeExportException(
                $"Resource '{entry.Id}' is an observation but declares runtime texture metadata.",
                []);
        }

        if (string.IsNullOrWhiteSpace(entry.LogicalPath) &&
            string.IsNullOrWhiteSpace(entry.ResolvedPath))
        {
            throw new UeExportException(
                $"Resource '{entry.Id}' is an observation but has no logical or resolved source path.",
                []);
        }

        var expectedId = UeResourceIdentity.CreateObservationId(
            entry.LogicalPath,
            entry.ResolvedPath,
            type);
        if (!string.Equals(entry.Id, expectedId, StringComparison.Ordinal))
        {
            throw new UeExportException(
                $"Resource '{entry.Id}' observation id does not match expected '{expectedId}'.",
                []);
        }
    }

    private static void ValidateObservation(
        UeExportObservation observation,
        IReadOnlyDictionary<string, UeResourceEntry> manifestById,
        NodeModelIdentities identities)
    {
        if (string.IsNullOrWhiteSpace(observation.Code))
        {
            throw new UeExportException(
                "Manifest contains an observation event with no code.",
                []);
        }

        if (string.IsNullOrWhiteSpace(observation.NodeId) ||
            !identities.NodeIds.Contains(observation.NodeId))
        {
            throw new UeExportException(
                $"Observation event '{observation.Code}' does not reference a known nodeId.",
                []);
        }

        if (!string.IsNullOrWhiteSpace(observation.ModelId) &&
            !identities.ModelIds.Contains(observation.ModelId))
        {
            throw new UeExportException(
                $"Observation event '{observation.Code}' references modelId '{observation.ModelId}' that is not present in the snapshot.",
                []);
        }

        if (!string.IsNullOrWhiteSpace(observation.ResourceId) &&
            !manifestById.ContainsKey(observation.ResourceId))
        {
            throw new UeExportException(
                $"Observation event '{observation.Code}' references resource id '{observation.ResourceId}' that is missing from the manifest.",
                []);
        }

        if (!string.IsNullOrWhiteSpace(observation.ResourceType) &&
            (!Enum.TryParse<UeResourceType>(observation.ResourceType, ignoreCase: true, out var type) ||
             type == UeResourceType.Unknown))
        {
            throw new UeExportException(
                $"Observation event '{observation.Code}' has unsupported resource type '{observation.ResourceType}'.",
                []);
        }
    }

    private static UeResourceType ParseKnownResourceType(string type)
    {
        if (!Enum.TryParse<UeResourceType>(type, ignoreCase: true, out var parsed) ||
            parsed == UeResourceType.Unknown)
        {
            throw new UeExportException(
                $"Manifest resource type '{type}' is not a supported UE resource type.",
                []);
        }

        return parsed;
    }

    private static void ValidateOrigin(UeResourceEntry entry)
    {
        var origin = ParseKnownOrigin(entry.Origin);
        if (entry.RuntimeTexture != null &&
            !string.Equals(origin, "runtimegenerated", StringComparison.Ordinal))
        {
            throw new UeExportException(
                $"Resource '{entry.Id}' has runtime texture metadata but origin '{entry.Origin}'; expected runtimegenerated.",
                []);
        }

        if (string.Equals(origin, "runtimegenerated", StringComparison.Ordinal) &&
            entry.RuntimeTexture == null)
        {
            throw new UeExportException(
                $"Resource '{entry.Id}' declares runtimegenerated origin without runtime texture metadata.",
                []);
        }
    }

    private static string ParseKnownOrigin(string origin)
    {
        if (!Enum.TryParse<UeResourceOrigin>(origin, ignoreCase: true, out var parsed))
        {
            throw new UeExportException(
                $"Resource origin '{origin}' is not a supported UE resource origin.",
                []);
        }

        if (parsed == UeResourceOrigin.Unknown &&
            !origin.Equals("unknown", StringComparison.OrdinalIgnoreCase))
        {
            throw new UeExportException(
                $"Resource origin '{origin}' is not a supported UE resource origin.",
                []);
        }

        return parsed.ToString().ToLowerInvariant();
    }

    private static void AssertNoPointerProperties(Type type)
    {
        AssertNoPointerProperties(
            type,
            new HashSet<Type>());
    }

    private static void AssertNoPointerProperties(Type type, HashSet<Type> visited)
    {
        if (type == typeof(nint) ||
            type == typeof(nuint) ||
            type == typeof(IntPtr) ||
            type == typeof(UIntPtr))
        {
            throw new UeExportException(
                $"UE snapshot type '{type.FullName}' contains a process pointer field.",
                []);
        }

        if (type.IsPrimitive ||
            type.IsEnum ||
            type == typeof(string) ||
            type == typeof(decimal) ||
            type == typeof(DateTime) ||
            type == typeof(JsonElement))
        {
            return;
        }

        if (Nullable.GetUnderlyingType(type) is { } nullableType)
        {
            AssertNoPointerProperties(nullableType, visited);
            return;
        }

        if (type.IsArray)
        {
            AssertNoPointerProperties(type.GetElementType()!, visited);
            return;
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
        {
            AssertNoPointerProperties(type.GetGenericArguments()[0], visited);
            return;
        }

        if (!visited.Add(type))
        {
            return;
        }

        foreach (var property in type.GetProperties(
                     BindingFlags.Public |
                     BindingFlags.NonPublic |
                     BindingFlags.Instance))
        {
            AssertNoPointerProperties(property.PropertyType, visited);
        }

        foreach (var field in type.GetFields(
                     BindingFlags.Public |
                     BindingFlags.NonPublic |
                     BindingFlags.Instance))
        {
            AssertNoPointerProperties(field.FieldType, visited);
        }
    }

    private static void ValidateSharedPhysicalFiles(UeResourceManifest manifest)
    {
        foreach (var group in manifest.Resources
                     .Where(x => x.File != null)
                     .GroupBy(
                         x => x.File!,
                         StringComparer.OrdinalIgnoreCase))
        {
            var hashes =
                group
                .Select(x => x.Sha256)
                .Where(x => x != null)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            if (hashes.Length > 1)
            {
                throw new UeExportException(
                    $"Physical resource file '{group.Key}' is shared by resources with different SHA256 values.",
                    []);
            }
        }
    }

    private sealed record NodeModelIdentities(
        IReadOnlySet<string> NodeIds,
        IReadOnlySet<string> ModelIds);
}
