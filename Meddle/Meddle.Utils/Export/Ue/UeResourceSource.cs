namespace Meddle.Utils.Export.Ue;

public enum UeResourceReadStatus
{
    Success,
    NotFound,
    ReadFailed,
    Unresolved,
    MissingPath
}

public enum UeResourceSourceKind
{
    Game,
    Disk,
    Malformed,
    Missing
}

public sealed record UeResolvedPath(
    string Original,
    string Trimmed,
    bool HadHandleDecoration,
    bool IsRooted,
    UeResourceSourceKind SourceKind)
{
    public static UeResolvedPath? TryParse(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var original = path;
        var trimmed = original;
        var hadHandleDecoration = original[0] == '|';
        if (hadHandleDecoration)
        {
            var separator = original.IndexOf('|', 1);
            if (separator < 0)
            {
                return new UeResolvedPath(original, original, true, false, UeResourceSourceKind.Malformed);
            }

            trimmed = original[(separator + 1)..];
        }

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return new UeResolvedPath(original, trimmed, hadHandleDecoration, false, UeResourceSourceKind.Malformed);
        }

        var isRooted = IsRootedPath(trimmed);
        return new UeResolvedPath(
            original,
            trimmed,
            hadHandleDecoration,
            isRooted,
            isRooted ? UeResourceSourceKind.Disk : UeResourceSourceKind.Game);
    }

    private static bool IsRootedPath(string path)
    {
        if (Path.IsPathRooted(path))
        {
            return true;
        }

        if (path.Length >= 3 &&
            char.IsAsciiLetter(path[0]) &&
            path[1] == ':' &&
            (path[2] == '\\' || path[2] == '/'))
        {
            return true;
        }

        return path.StartsWith("\\\\", StringComparison.Ordinal);
    }
}

public sealed record UeResourceReadRequest(
    string? LogicalPath,
    string? ResolvedPath,
    UeResourceType Type);

public sealed record UeResourceReadResult(
    byte[]? Data,
    UeResourceReadStatus Status,
    UeResourceOrigin Origin,
    Exception? Error = null)
{
    public static UeResourceReadResult Success(byte[] data, UeResourceOrigin origin)
    {
        return new UeResourceReadResult(data, UeResourceReadStatus.Success, origin);
    }

    public static UeResourceReadResult NotFound(UeResourceOrigin origin = UeResourceOrigin.Unknown)
    {
        return new UeResourceReadResult(null, UeResourceReadStatus.NotFound, origin);
    }

    public static UeResourceReadResult ReadFailed(Exception error, UeResourceOrigin origin = UeResourceOrigin.Unknown)
    {
        return new UeResourceReadResult(null, UeResourceReadStatus.ReadFailed, origin, error);
    }

    public static UeResourceReadResult Unresolved()
    {
        return new UeResourceReadResult(null, UeResourceReadStatus.Unresolved, UeResourceOrigin.Unknown);
    }

    public static UeResourceReadResult MissingPath()
    {
        return new UeResourceReadResult(null, UeResourceReadStatus.MissingPath, UeResourceOrigin.Unknown);
    }
}

public static class UeResourcePathPolicy
{
    public static string? GetEffectivePath(string? logicalPath, string? resolvedPath)
    {
        return string.IsNullOrWhiteSpace(resolvedPath) ? logicalPath : resolvedPath;
    }

    public static UeResolvedPath? Parse(string? effectivePath)
    {
        return UeResolvedPath.TryParse(effectivePath);
    }

    public static UeResourceSourceKind Classify(string? effectivePath)
    {
        return UeResolvedPath.TryParse(effectivePath)?.SourceKind ?? UeResourceSourceKind.Missing;
    }
}

public sealed class UeResourceSource
{
    private readonly Func<string, byte[]?> readSqPack;
    private readonly Func<string, byte[]?> readDisk;

    public UeResourceSource(
        Func<string, byte[]?> readSqPack,
        Func<string, byte[]?> readDisk)
    {
        this.readSqPack = readSqPack;
        this.readDisk = readDisk;
    }

    public UeResourceReadResult Read(UeResourceReadRequest request)
    {
        var effectivePath = UeResourcePathPolicy.GetEffectivePath(request.LogicalPath, request.ResolvedPath);
        var resolvedPath = UeResourcePathPolicy.Parse(effectivePath);
        if (resolvedPath == null)
        {
            return UeResourceReadResult.MissingPath();
        }

        if (resolvedPath.SourceKind == UeResourceSourceKind.Malformed)
        {
            return UeResourceReadResult.Unresolved();
        }

        var isDisk = resolvedPath.SourceKind == UeResourceSourceKind.Disk;
        var origin = isDisk ? UeResourceOrigin.Modded : UeResourceOrigin.Game;
        try
        {
            var data = isDisk
                ? readDisk(resolvedPath.Trimmed)
                : readSqPack(resolvedPath.Trimmed);
            if (data == null)
            {
                return UeResourceReadResult.NotFound(origin);
            }

            return UeResourceReadResult.Success(data, origin);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return UeResourceReadResult.NotFound(origin);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return UeResourceReadResult.ReadFailed(ex, origin);
        }
    }
}
