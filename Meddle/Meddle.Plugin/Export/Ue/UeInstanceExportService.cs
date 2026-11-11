using Meddle.Plugin.Models.Composer;
using Meddle.Plugin.Models.Layout;
using Meddle.Plugin.Services;
using Meddle.Plugin.UI.Layout;
using Meddle.Plugin.Utils;
using Meddle.Utils.Export.Ue;
using SharpGLTF.Scenes;

namespace Meddle.Plugin.Export.Ue;

public sealed class UeInstanceExportService(ComposerFactory composerFactory) : IService
{
    public static bool HasGeometry(ParsedInstance instance) => instance switch
    {
        ParsedCharacterInstance or ParsedBgPartsInstance or ParsedTerrainInstance => true,
        ParsedSharedInstance shared => shared.Children.Any(HasGeometry),
        _ => false
    };

    public static ParsedInstance[] ExportRoots(IEnumerable<ParsedInstance> instances)
    {
        var selected = instances.DistinctBy(i => i.Id).Where(HasGeometry).ToArray();
        // Flatten includes the group itself. Exclude only descendants of another selection.
        var children = selected.OfType<ParsedSharedInstance>()
            .SelectMany(s => s.Flatten().Where(i => i.Id != s.Id)).Select(i => i.Id).ToHashSet();
        return selected.Where(i => !children.Contains(i.Id) &&
            !(i is ParsedCharacterInstance { Parent: not null } character && selected.Any(p => p.Id == character.Parent.Id))).ToArray();
    }

    public void Export(ParsedInstance[] instances, string destination, Configuration.ExportConfiguration settings,
        UeMeshMergeMode mergeMode, CancellationToken token, ProgressWrapper wrapper)
    {
        var roots = ExportRoots(instances);
        if (roots.Length == 0) throw new InvalidDataException("Selected instances contain no supported mesh geometry.");
        var progress = new ExportProgress(roots.Length, "UE instances");
        wrapper.Progress = progress;
        var staging = Path.Combine(Path.GetTempPath(), "MeddleNative", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            settings = settings.Clone();
            settings.PoseMode = SkeletonUtils.PoseMode.None;
            settings.UseDeformer = true;
            var composer = composerFactory.CreateComposer(staging, settings, token);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var instance in roots)
            {
                token.ThrowIfCancellationRequested();
                var scene = new SceneBuilder();
                if (composer.ComposeInstance(instance, scene, progress) == null)
                    throw new InvalidDataException($"Instance {instance.Id} produced no geometry. Check visibility and export filters.");
                token.ThrowIfCancellationRequested();
                var displayName = instance switch
                {
                    ParsedCharacterInstance character => character.Name,
                    ParsedSharedInstance shared when !string.IsNullOrWhiteSpace(shared.Name) => shared.Name,
                    IPathInstance path => Path.GetFileNameWithoutExtension(path.Path.GamePath),
                    _ => instance.Type.ToString()
                };
                var name = NativeAssetExporter.SafeName(displayName);
                var uniqueName = name;
                var suffix = 1;
                while (!names.Add(uniqueName)) uniqueName = $"{name}_{suffix++}";
                NativeAssetExporter.Export(scene.ToGltf2(), destination, uniqueName, Path.Combine(staging, "cache"),
                    token: token, mergeMode: mergeMode);
                progress.IncrementProgress();
            }
        }
        finally
        {
            progress.IsComplete = true;
            var stagingRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "MeddleNative")) + Path.DirectorySeparatorChar;
            if (Path.GetFullPath(staging).StartsWith(stagingRoot, StringComparison.OrdinalIgnoreCase))
                Directory.Delete(staging, true);
        }
    }
}
