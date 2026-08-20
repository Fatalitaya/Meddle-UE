using Meddle.Utils.Files.SqPack;

namespace Meddle.Utils.Helpers;

public static class PathUtil
{
    public static byte[]? GetFileOrReadFromDisk(this SqPack pack, string path)
    {
        if (!path.TryParseHandlePath(out var lookupPath))
        {
            return null;
        }
        
        // if path is rooted, get from disk
        if (Path.IsPathRooted(lookupPath))
        {
            var data = File.ReadAllBytes(lookupPath);
            return data;
        }

        var fileDescriptor = pack.GetFile(lookupPath);
        return fileDescriptor?.File.RawData.ToArray();
    }
    
    public static string TrimHandlePath(this string path)
    {
        // if path is in format |...|path/to/file, trim the |...| part
        if (!string.IsNullOrEmpty(path) && path[0] == '|')
        {
            var separator = path.IndexOf('|', 1);
            if (separator >= 0)
            {
                path = path[(separator + 1)..];
            }
        }

        return path;
    }

    public static bool TryParseHandlePath(this string path, out string trimmed)
    {
        trimmed = path;
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        if (path[0] != '|')
        {
            return true;
        }

        var separator = path.IndexOf('|', 1);
        if (separator < 0)
        {
            return false;
        }

        trimmed = path[(separator + 1)..];
        return !string.IsNullOrWhiteSpace(trimmed);
    }
}
