using SharpGLTF.Schema2;

namespace Meddle.Utils.Export.Ue;

public enum UeMeshMergeMode { All, ByPart, None }

internal sealed record UeMeshGroup(string Name, Node[] Nodes);

internal static class UeMeshGrouping
{
    public static UeMeshGroup[] Build(ModelRoot scene, UeMeshMergeMode mode)
    {
        var nodes = scene.LogicalNodes.Where(n => n.Mesh != null).ToArray();
        var groups = mode switch
        {
            UeMeshMergeMode.All => nodes.Length == 0 ? [] : [new("All", nodes)],
            UeMeshMergeMode.ByPart => nodes.GroupBy(n =>
                (Owner: n.Mesh.Extras?["modelOwner"]?.GetValue<string>() ?? (n.Skin != null ? "0" : n.VisualParent?.LogicalIndex.ToString() ?? "0"),
                 Part: n.Mesh.Extras?["modelPart"]?.GetValue<string>() ??
                       Path.GetFileNameWithoutExtension(n.Mesh.Extras?["modelGamePath"]?.GetValue<string>() ?? n.Mesh.Name ?? "Model")))
                .Select(g => new UeMeshGroup(g.Key.Owner == "0" || g.All(n=>n.Skin == null) ? g.Key.Part : $"{g.Key.Part}_Attach{g.Key.Owner}",g.ToArray())).ToArray(),
            UeMeshMergeMode.None => nodes.Select(n => new UeMeshGroup(n.Mesh.Name ?? "Mesh",[n])).ToArray(),
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return groups.Select(g =>
        {
            var name=NativeAssetExporter.SafeName(g.Name); var candidate=name; var suffix=1;
            while(!names.Add(candidate)) candidate=$"{name}_{suffix++}";
            return g with { Name=candidate };
        }).ToArray();
    }
}
