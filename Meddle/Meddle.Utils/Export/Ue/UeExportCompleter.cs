namespace Meddle.Utils.Export.Ue;

public static class UeExportCompleter
{
    public static string Complete(
        UeCharacterSnapshot snapshot,
        UeResourceBundle bundle,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (snapshot == null)
        {
            throw new UeExportException("Snapshot is null.", []);
        }

        if (bundle == null)
        {
            throw new UeExportException("Resource bundle is null.", []);
        }

        var finalDirectory = Path.GetFullPath(destinationPath);
        var finalDirectoryName = Path.GetFileName(finalDirectory);
        if (string.IsNullOrWhiteSpace(finalDirectoryName))
        {
            throw new ArgumentException("Destination path must name a bundle directory.", nameof(destinationPath));
        }

        var parentDirectory = Path.GetDirectoryName(finalDirectory);
        if (string.IsNullOrWhiteSpace(parentDirectory))
        {
            parentDirectory = Directory.GetCurrentDirectory();
        }

        UeExportPaths.EnsureDestinationDoesNotExist(finalDirectory);
        var tempDirectory = Path.Combine(parentDirectory, $".{finalDirectoryName}.tmp-{Guid.NewGuid():N}");

        Directory.CreateDirectory(tempDirectory);
        try
        {
            var resourcesDirectory = Path.Combine(tempDirectory, "resources");
            Directory.CreateDirectory(resourcesDirectory);
            var writtenFileShas = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in bundle.Manifest.Resources.Where(x => x.Availability == UeResourceAvailability.Available))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relativeFile = entry.File;
                if (string.IsNullOrWhiteSpace(relativeFile) ||
                    !relativeFile.StartsWith("resources/", StringComparison.Ordinal) ||
                    relativeFile.IndexOf('/', "resources/".Length) >= 0)
                {
                    throw new UeExportException(
                        $"Resource '{entry.Id}' has an invalid flat file path '{relativeFile}'.",
                        bundle.Manifest.Observations);
                }

                var fileName = relativeFile["resources/".Length..];
                if (writtenFileShas.TryGetValue(fileName, out var existingSha) &&
                    !string.Equals(existingSha, entry.Sha256, StringComparison.Ordinal))
                {
                    throw new UeExportException(
                        $"Physical resource file '{relativeFile}' is referenced by multiple SHA-256 values.",
                        bundle.Manifest.Observations);
                }

                if (!bundle.DataById.TryGetValue(entry.Id, out var bytes))
                {
                    throw new UeExportException(
                        $"Resource '{entry.Id}' is declared available but has no captured bytes.",
                        bundle.Manifest.Observations);
                }

                writtenFileShas[fileName] = entry.Sha256;
                var filePath = Path.Combine(resourcesDirectory, fileName);
                File.WriteAllBytes(filePath, bytes);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.WriteAllText(
                Path.Combine(tempDirectory, "manifest.json"),
                UeExportJson.Serialize(bundle.Manifest));
            cancellationToken.ThrowIfCancellationRequested();
            File.WriteAllText(
                Path.Combine(tempDirectory, "character.json"),
                UeExportJson.Serialize(snapshot));

            cancellationToken.ThrowIfCancellationRequested();
            UeExportValidator.Validate(snapshot, bundle.Manifest, tempDirectory);

            cancellationToken.ThrowIfCancellationRequested();
            File.WriteAllText(Path.Combine(tempDirectory, "bundle.complete"), "complete");

            Directory.Move(tempDirectory, finalDirectory);
            return finalDirectory;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (UeExportException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new UeExportException(
                "UE export failed unexpectedly.",
                bundle.Manifest.Observations,
                ex);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, true);
            }
        }
    }
}
