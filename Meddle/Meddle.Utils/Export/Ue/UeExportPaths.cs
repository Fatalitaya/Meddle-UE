namespace Meddle.Utils.Export.Ue;

public static class UeExportPaths
{
    public static void EnsureDestinationDoesNotExist(string destinationPath)
    {
        if (File.Exists(destinationPath) || Directory.Exists(destinationPath))
        {
            throw new UeExportException(
                $"Destination already exists; refusing to overwrite: {destinationPath}",
                []);
        }
    }
}
