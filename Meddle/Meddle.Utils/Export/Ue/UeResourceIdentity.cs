using System.Security.Cryptography;
using System.Text;

namespace Meddle.Utils.Export.Ue;

public static class UeResourceIdentity
{
    public static string GetSha256(ReadOnlySpan<byte> data)
    {
        return Convert.ToHexStringLower(SHA256.HashData(data));
    }

    public static string NormalizeExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return string.Empty;
        }

        return extension.StartsWith('.') ? extension.ToLowerInvariant() : $".{extension.ToLowerInvariant()}";
    }

    public static UeResourceType GetResourceTypeFromPath(string path)
    {
        var extension = NormalizeExtension(Path.GetExtension(TrimHandlePrefix(path)));
        return extension switch
        {
            ".mdl" => UeResourceType.Mdl,
            ".mtrl" => UeResourceType.Mtrl,
            ".tex" => UeResourceType.Tex,
            ".sklb" => UeResourceType.Sklb,
            ".pbd" => UeResourceType.Pbd,
            ".shpk" => UeResourceType.Shpk,
            _ => UeResourceType.Unknown
        };
    }

    public static string GetExtensionForType(UeResourceType type)
    {
        return type switch
        {
            UeResourceType.Mdl => ".mdl",
            UeResourceType.Mtrl => ".mtrl",
            UeResourceType.Tex => ".tex",
            UeResourceType.Sklb => ".sklb",
            UeResourceType.Pbd => ".pbd",
            UeResourceType.Shpk => ".shpk",
            _ => string.Empty
        };
    }

    public static string CreateResourceId(string sha256, UeResourceType type, UeRuntimeTextureMetadata? runtimeTexture = null)
    {
        var typeName = type.ToString().ToLowerInvariant();
        return runtimeTexture == null
            ? $"sha256:{sha256}:{typeName}"
            : $"sha256:{sha256}:{typeName}:{runtimeTexture.Encoding}";
    }

    public static string CreateObservationId(string? logicalPath, string? resolvedPath, UeResourceType type)
    {
        var key = $"{type}|{logicalPath ?? string.Empty}|{resolvedPath ?? string.Empty}";
        return $"observation:{GetSha256(Encoding.UTF8.GetBytes(key))}";
    }

    public static UeResourceOrigin ClassifyOrigin(string? resolvedPath)
    {
        var parsed = UeResolvedPath.TryParse(resolvedPath);
        if (parsed == null)
        {
            return UeResourceOrigin.Unknown;
        }

        return parsed.SourceKind switch
        {
            UeResourceSourceKind.Disk => UeResourceOrigin.Modded,
            UeResourceSourceKind.Game => UeResourceOrigin.Game,
            _ => UeResourceOrigin.Unknown
        };
    }

    public static string TrimHandlePrefix(string path)
    {
        if (string.IsNullOrEmpty(path) || path[0] != '|')
        {
            return path;
        }

        var separator = path.IndexOf('|', 1);
        return separator >= 0 ? path[(separator + 1)..] : path;
    }
}

public enum UeResourceType
{
    Mdl,
    Mtrl,
    Tex,
    Sklb,
    Pbd,
    Shpk,
    Unknown
}

public enum UeResourceOrigin
{
    Game,
    Modded,
    RuntimeGenerated,
    Unknown
}
