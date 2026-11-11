using Meddle.Plugin.Models.Layout;
using Meddle.Plugin.Services;
using Meddle.Utils.Export.Ue;
using Meddle.SqPack;
using Microsoft.Extensions.Logging;

namespace Meddle.Plugin.Export.Ue;

public sealed record UeExportResult(string OutputPath);

public sealed class UeCharacterExportService : IService
{
    private readonly SqPack.SqPack pack;
    private readonly ILogger<UeCharacterExportService> logger;

    public UeCharacterExportService(SqPack.SqPack pack, ILogger<UeCharacterExportService> logger)
    {
        this.pack = pack;
        this.logger = logger;
    }

    public UeExportResult Export(
        ParsedCharacterInfo characterInfo,
        string destinationPath)
    {
        return Export(
            characterInfo,
            displayName: string.Empty,
            objectKind: string.Empty,
            destinationPath,
            CancellationToken.None);
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
            throw new ArgumentException("Destination path must name an asset directory.", nameof(destinationPath));
        }

        try
        {
            var staging = Path.Combine(Path.GetTempPath(), "MeddleNative", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            try
            {
                var settings = new Configuration.ExportConfiguration { PoseMode = Meddle.Plugin.Utils.SkeletonUtils.PoseMode.None, UseDeformer = true };
                var composer = new Meddle.Plugin.Models.Composer.CharacterComposer(pack, settings, staging, cancellationToken);
                var scene = new SharpGLTF.Scenes.SceneBuilder();
                var root = new SharpGLTF.Scenes.NodeBuilder("Character");
                scene.AddNode(root);
                if (composer.Compose(characterInfo, scene, root, new Meddle.Plugin.Models.Composer.ExportProgress(characterInfo.Models.Count, "UE assets")) == null)
                    throw new InvalidDataException("Character composition did not produce a skeleton.");
                cancellationToken.ThrowIfCancellationRequested();
                NativeAssetExporter.Export(scene.ToGltf2(), finalDirectory,
                    string.IsNullOrWhiteSpace(displayName) ? finalDirectoryName : displayName,
                    Path.Combine(staging, "cache"), token: cancellationToken);
                return new UeExportResult(finalDirectory);
            }
            finally
            {
                var stagingRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "MeddleNative")) + Path.DirectorySeparatorChar;
                if (Path.GetFullPath(staging).StartsWith(stagingRoot, StringComparison.OrdinalIgnoreCase))
                    Directory.Delete(staging, true);
            }

        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("UE export cancelled.");
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "UE export failed.");
            throw;
        }
    }

}
