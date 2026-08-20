using Meddle.Plugin.Models.Layout;
using Meddle.Plugin.Services;
using Meddle.Utils.Export.Ue;
using Meddle.Utils.Files.SqPack;
using Microsoft.Extensions.Logging;

namespace Meddle.Plugin.Export.Ue;

public sealed record UeExportResult(
    string OutputPath,
    IReadOnlyList<UeExportObservation> Observations);

public sealed class UeCharacterExportService : IService
{
    private readonly SqPack pack;
    private readonly ILogger<UeCharacterExportService> logger;

    public UeCharacterExportService(SqPack pack, ILogger<UeCharacterExportService> logger)
    {
        this.pack = pack;
        this.logger = logger;
    }

    public UeExportResult Export(
        ParsedCharacterInfo characterInfo,
        string displayName,
        string objectKind,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var finalDirectory = Path.GetFullPath(destinationPath);
        var finalDirectoryName = Path.GetFileName(finalDirectory);
        if (string.IsNullOrWhiteSpace(finalDirectoryName))
        {
            throw new ArgumentException("Destination path must name a bundle directory.", nameof(destinationPath));
        }

        try
        {
            var builder = new UeSnapshotBuilder(new SqPackUeResourceReader(pack), cancellationToken);
            var snapshot = builder.Build(characterInfo, displayName, objectKind);
            if (snapshot == null)
            {
                throw new UeExportException("Failed to build UE snapshot.", builder.Observations);
            }

            var bundle = builder.BuildBundle();
            UeExportCompleter.Complete(snapshot, bundle, finalDirectory, cancellationToken);

            LogObservations(logger, bundle.Manifest.Observations);

            return new UeExportResult(
                finalDirectory,
                bundle.Manifest.Observations);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("UE export cancelled.");
            throw;
        }
        catch (UeExportException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "UE export failed.");
            throw new UeExportException("UE export failed unexpectedly.", [], ex);
        }
    }

    private static void LogObservations(
        ILogger<UeCharacterExportService> logger,
        IReadOnlyList<UeExportObservation> observations)
    {
        foreach (var observation in observations)
        {
            logger.LogInformation(
                "UE export observation {Code}: {Message} ({LogicalPath} / {ResolvedPath})",
                observation.Code,
                observation.Message,
                observation.LogicalPath,
                observation.ResolvedPath);
        }
    }
}
