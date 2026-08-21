namespace Meddle.Utils.Export.Ue;

/// <summary>
/// Stateful allocator. One instance must be used per manifest generation.
/// </summary>
public sealed class UeResourceFileNameBuilder
{
    public const int MaxBaseNameLength = 120;

    private readonly Dictionary<string, string> allocatedFileNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> allocatedContent = new(StringComparer.OrdinalIgnoreCase);

    public string Build(
        UeResourceType type,
        string? originalPath,
        string sha256,
        string? extension = null,
        string? fallbackPath = null,
        string? runtimeKind = null)
    {
        if (string.IsNullOrWhiteSpace(sha256) || sha256.Length < 64)
        {
            throw new ArgumentException("SHA-256 must be a 64-character hexadecimal string.", nameof(sha256));
        }

        var normalizedSha = sha256.ToLowerInvariant();
        var baseName = GetBaseName(
            type,
            originalPath,
            fallbackPath,
            runtimeKind);
        var normalizedExtension = UeResourceIdentity.NormalizeExtension(
            string.IsNullOrWhiteSpace(extension) ? GetExtension(type, originalPath, fallbackPath, runtimeKind) : extension);
        var contentKey = $"{normalizedSha}|{normalizedExtension}";
        if (allocatedContent.TryGetValue(contentKey, out var existingFileName))
        {
            return existingFileName;
        }

        var candidates = new[]
        {
            $"{baseName}_{normalizedSha[..8]}{normalizedExtension}",
            $"{baseName}_{normalizedSha[..16]}{normalizedExtension}",
            $"{baseName}_{normalizedSha}{normalizedExtension}"
        };

        foreach (var candidate in candidates)
        {
            if (allocatedFileNames.TryGetValue(candidate, out var existingSha))
            {
                if (string.Equals(existingSha, normalizedSha, StringComparison.Ordinal))
                {
                    return candidate;
                }

                continue;
            }

            allocatedFileNames[candidate] = normalizedSha;
            allocatedContent[contentKey] = candidate;
            return candidate;
        }

        throw new InvalidOperationException($"Unable to allocate a unique physical filename for SHA-256 '{normalizedSha}'.");
    }

    private static string GetBaseName(
        UeResourceType type,
        string? originalPath,
        string? fallbackPath,
        string? runtimeKind)
    {
        if (runtimeKind != null)
        {
            return $"runtime_{SanitizeBaseName(GetRuntimeKind(runtimeKind))}";
        }

        var pathBaseName = GetPathBaseName(originalPath);

        if (string.IsNullOrEmpty(pathBaseName) &&
            !string.Equals(originalPath, fallbackPath, StringComparison.Ordinal))
        {
            pathBaseName = GetPathBaseName(fallbackPath);
        }

        if (!string.IsNullOrEmpty(pathBaseName))
        {
            return pathBaseName;
        }

        return type == UeResourceType.Unknown
            ? "resource"
            : type.ToString().ToLowerInvariant();
    }

    private static string? GetPathBaseName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var trimmed = UeResourceIdentity.TrimHandlePrefix(path);
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed[0] == '|')
        {
            return null;
        }

        var baseName = Path.GetFileNameWithoutExtension(Path.GetFileName(trimmed));
        return string.IsNullOrWhiteSpace(baseName) ? null : SanitizeBaseName(baseName);
    }

    private static string GetExtension(
        UeResourceType type,
        string? originalPath,
        string? fallbackPath,
        string? runtimeKind)
    {
        if (runtimeKind != null)
        {
            return ".png";
        }

        var extension = GetPathExtension(originalPath);
        if (string.IsNullOrEmpty(extension) &&
            !string.Equals(originalPath, fallbackPath, StringComparison.Ordinal))
        {
            extension = GetPathExtension(fallbackPath);
        }

        return !string.IsNullOrEmpty(extension)
            ? extension
            : UeResourceIdentity.GetExtensionForType(type);
    }

    private static string? GetPathExtension(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var extension = UeResourceIdentity.NormalizeExtension(
            Path.GetExtension(UeResourceIdentity.TrimHandlePrefix(path)));
        return string.IsNullOrEmpty(extension) ? null : extension;
    }

    private static string GetRuntimeKind(string runtimeKind)
    {
        if (string.IsNullOrWhiteSpace(runtimeKind))
        {
            return "texture";
        }

        var normalized = runtimeKind
            .Trim()
            .ToLowerInvariant();

        if (normalized.Contains("decal", StringComparison.Ordinal))
        {
            return "decal";
        }

        if (normalized.Contains("texture", StringComparison.Ordinal) ||
            normalized.Contains("tex", StringComparison.Ordinal))
        {
            return "texture";
        }

        return "resource";
    }

    private static string SanitizeBaseName(string baseName)
    {
        var sanitized = new string(baseName.Select(c => IsInvalidFileNameCharacter(c) ? '_' : c).ToArray())
            .Trim();
        return sanitized.Length > MaxBaseNameLength
            ? sanitized[..MaxBaseNameLength]
            : sanitized;
    }

    private static bool IsInvalidFileNameCharacter(char c)
    {
        return c < 32 ||
               c == '<' ||
               c == '>' ||
               c == ':' ||
               c == '"' ||
               c == '/' ||
               c == '\\' ||
               c == '|' ||
               c == '?' ||
               c == '*';
    }
}
