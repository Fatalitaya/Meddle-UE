namespace Meddle.Unreal;

/// <summary>Small UE expression description; deliberately not a Blender graph format.</summary>
public sealed class MaterialGraph
{
    public List<MaterialNode> Nodes { get; } = [];
    public Dictionary<string, MaterialLink> Outputs { get; } = [];
    public MaterialLink Add(MaterialNode node, int mask = 0)
    {
        Nodes.Add(node);
        return new(Nodes.Count + 2, mask);
    }
    public MaterialLink Uv(int channel) => Add(new MaterialUv(channel));
    public MaterialLink VertexColor() => Add(new MaterialVertexColor(), 15);
    public MaterialLink Sample(string path, bool srgb, MaterialLink uv, bool normal = false, MaterialLink? mipBias = null)
        => Add(new MaterialSample(path, srgb, uv, normal, mipBias), 15);
    public MaterialLink Custom(string label, string code, int dimensions, params (string Name, MaterialLink Link)[] inputs)
        => Add(new MaterialCustom(label, code, dimensions, inputs));
}

public readonly record struct MaterialLink(int Expression, int Mask = 0, int Output = 0);
public abstract record MaterialNode;
public sealed record MaterialUv(int Channel) : MaterialNode;
public sealed record MaterialVertexColor : MaterialNode;
public sealed record MaterialWorldPosition : MaterialNode;
public sealed record MaterialVertexNormal : MaterialNode;
public sealed record MaterialVertexInterpolator(MaterialLink Input) : MaterialNode;
public sealed record MaterialSample(string Path, bool Srgb, MaterialLink Uv, bool Normal = false, MaterialLink? MipBias = null) : MaterialNode;
public sealed record MaterialCustom(string Label, string Code, int Dimensions,
    (string Name, MaterialLink Link)[] Inputs) : MaterialNode;
