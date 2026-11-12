using Meddle.Plugin.Models.Composer;
using Meddle.Plugin.Models.Layout;
using Meddle.Plugin.Services;
using Meddle.Plugin.UI.Layout;
using Meddle.Plugin.Utils;
using Meddle.Utils.Export.Ue;
using SharpGLTF.Scenes;
using System.Numerics;
using Meddle.Unreal;
using Meddle.Utils.Export;
using Meddle.Plugin.Models;

namespace Meddle.Plugin.Export.Ue;

public sealed class UeInstanceExportService(ComposerFactory composerFactory) : IService
{
    public void ExportLevel(TerritoryExportGroup<ParsedInstance>[] groups, string destination, string name,
        Configuration.ExportConfiguration settings, CancellationToken token, ProgressWrapper wrapper)
    {
        name = NativeAssetExporter.SafeName(name);
        var instances = groups.SelectMany(g => g.Items).ToArray();
        if (instances.Any(i => i is not ParsedBgPartsInstance))
            throw new InvalidDataException("Territory 子关卡 要求已展开的静态模型实例。");
        var parts = instances.Cast<ParsedBgPartsInstance>().ToArray();
        var resources = TerritoryGrouping.Build(parts.Select(p => new TerritoryGroupItem<ParsedBgPartsInstance>(p,
            p.Path.FullPath, p.BgChangeMaterial?.MaterialPath ?? "", p.BgChangeMaterial?.BGChangeMaterialIndex ?? -1,
            p.Stain?.RowId ?? 0, "", p.BgStainLinear, p.LayoutColor, p.BgEmissivePower)), TerritoryGroupMode.RepeatedResource);
        var progress = new ExportProgress(resources.Length, "UE 子关卡资源");
        wrapper.Progress = progress;
        var staging = Path.Combine(Path.GetTempPath(), "MeddleNative", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            var composer = composerFactory.CreateComposer(staging, settings.Clone(), token);
            var geometry = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var placements = new Dictionary<nint, (string Mesh, string Appearance, string[] Materials)>();
            foreach (var resource in resources)
            {
                token.ThrowIfCancellationRequested();
                var source = resource.Items[0];
                // Compose at identity: the source model origin stays in the mesh asset.
                var local = new ParsedBgPartsInstance(source.Id, true, new Transform(Vector3.Zero, Quaternion.Identity, Vector3.One),
                    source.Path.FullPath, source.BgChangeMaterial)
                {
                    Stain = source.Stain, ModelPtr = source.ModelPtr, BgStainLinear = source.BgStainLinear,
                    LayoutColor = source.LayoutColor, BgEmissivePower = source.BgEmissivePower
                };
                var scene = new SceneBuilder();
                if (composer.ComposeInstance(local, scene, progress) == null)
                    throw new InvalidDataException($"无法生成子关卡模型 {source.Path.FullPath}");
                var model = scene.ToGltf2();
                var assetName = name + "_" + NativeAssetExporter.SafeName(resource.Name);
                // Some shader families encode vertex colors differently. Only share
                // geometry when their per-section vertex conversion also agrees.
                var colorEncoding = model.LogicalNodes.Where(n => n.Mesh != null).SelectMany(n => n.Mesh.Primitives)
                    .Select(p => CharacterMaterial.Classify(p.Material?.Extras?["ShaderPackage"]?.GetValue<string>() ?? "")
                        is CharacterShader.Legacy or CharacterShader.Tattoo or CharacterShader.Occlusion or CharacterShader.Skin);
                var key = source.Path.FullPath.Replace('\\', '/') + "|" + string.Join(',', colorEncoding);
                var reused = geometry.TryGetValue(key, out var meshPath);
                var materials = new List<string>();
                NativeAssetExporter.Export(model, destination, assetName, Path.Combine(staging, "cache"),
                    token: token, writeStaticMeshes: !reused, staticMaterialSlots: materials);
                if (!reused)
                    geometry[key] = meshPath = $"/Game/Meddle/{assetName}/Meshes/SM_{assetName}";
                foreach (var item in resource.Items) placements.Add(item.Id, (meshPath!, assetName, materials.ToArray()));
                progress.IncrementProgress();
            }
            var actors = groups.Select(g =>
            {
                var origin = Coordinates.Position(g.Items[0].Transform.Translation);
                var components = g.Items.Cast<ParsedBgPartsInstance>().GroupBy(p =>
                    (placements[p.Id].Appearance, Mirrored: p.Transform.AffineTransform.Matrix.GetDeterminant() < 0))
                    .Select(bucket =>
                    {
                        var resource = placements[bucket.First().Id];
                        var transforms = bucket.Select(p =>
                        {
                            var matrix = Coordinates.InstanceMatrix(p.Transform.AffineTransform.Matrix);
                            matrix.Translation -= origin;
                            return matrix;
                        }).ToArray();
                        return new LevelMeshInstances(bucket.Key.Appearance, resource.Mesh, transforms, resource.Materials);
                    }).ToArray();
                return new LevelActor(g.Name, origin, components);
            }).ToArray();
            token.ThrowIfCancellationRequested();
            LevelWriter.Write(Path.Combine(destination, "Content", "Meddle", name, name + ".umap"),
                "/Game/Meddle/" + name + "/" + name, actors);
        }
        finally
        {
            progress.IsComplete = true;
            Directory.Delete(staging, true);
        }
    }

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
