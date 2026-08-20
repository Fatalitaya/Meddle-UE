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

        if (manifest.SchemaVersion != UeExportSchema.CurrentVersion)
        {
            throw new UeExportException($"Unsupported manifest schema version {manifest.SchemaVersion}; current version is {UeExportSchema.CurrentVersion}.", []);
        }

        AssertNoPointerProperties(snapshot.GetType());

        var json = UeExportJson.Serialize(snapshot);
        if (json.Contains("ModelAddress", StringComparison.Ordinal) ||
            json.Contains("IntPtr", StringComparison.Ordinal))
        {
            throw new UeExportException("Snapshot serialized output contains an unstable pointer value.", []);
        }

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

        foreach (var observation in manifest.Observations)
        {
            ValidateObservation(observation, manifestById);
        }
    }

    public static IReadOnlyCollection<string> CollectReferencedResourceIds(UeCharacterSnapshot snapshot)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        AddSnapshot(snapshot);
        return ids;

        void AddSnapshot(UeCharacterSnapshot current)
        {
            foreach (var model in current.Models)
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
                    Add(model.Deformation.PbdResourceId);
                }
            }

            if (current.Skeleton != null)
            {
                foreach (var partial in current.Skeleton.PartialSkeletons)
                {
                    Add(partial.ResourceId);
                }
            }

            if (current.Attach != null)
            {
                if (current.Attach.OwnerSkeleton != null)
                {
                    AddSkeleton(current.Attach.OwnerSkeleton);
                }

                if (current.Attach.TargetSkeleton != null)
                {
                    AddSkeleton(current.Attach.TargetSkeleton);
                }
            }

            foreach (var child in current.Attaches)
            {
                AddSnapshot(child);
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

    private static void ValidateEntry(UeResourceEntry entry, string outputDirectory)
    {
        if (string.IsNullOrWhiteSpace(entry.Id))
        {
            throw new UeExportException("Manifest contains a resource entry with no id.", []);
        }

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
        IReadOnlyDictionary<string, UeResourceEntry> manifestById)
    {
        if (string.IsNullOrWhiteSpace(observation.Code))
        {
            throw new UeExportException(
                "Manifest contains an observation event with no code.",
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

    private static void ValidateSharedPhysicalFiles(
    UeResourceManifest manifest)
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
}
