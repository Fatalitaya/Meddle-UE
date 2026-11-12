using Meddle.Formats.Files;
using System.Buffers.Binary;
using System.Text;

if (args.Length < 1) throw new ArgumentException("TerritoryProbe <game directory> [scene paths...]");
using var pack = new Meddle.SqPack.SqPack(args[0]);
byte[]? Read(string path) => pack.GetFile(path)?.File.RawData.ToArray();
var paths = args.Skip(1).ToList();
if (paths.Count == 0)
{
    // Read just the Bg string column; the EXH supplies its offset and the fixed row size.
    var header = Read("exd/territorytype.exh") ?? throw new FileNotFoundException("TerritoryType");
    var columns = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(8));
    var pages = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(10));
    var fixedSize = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(6));
    var bgOffset = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(38));
    for (var i = 0; i < pages; i++)
    {
        var page = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(32 + columns * 4 + i * 8));
        var bytes = Read($"exd/territorytype_{page}.exd");
        if (bytes == null) continue;
        var indexSize = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(8));
        for (var entry = 32; entry < 32 + indexSize; entry += 8)
        {
            var row = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(entry + 4)) + 6;
            var offset = row + fixedSize + BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(row + bgOffset));
            var end = Array.IndexOf(bytes, (byte)0, offset);
            var bg = Encoding.UTF8.GetString(bytes, offset, end - offset);
            if (bg.Length == 0) continue;
            paths.Add((bg.StartsWith("bg/") ? bg : "bg/" + bg) + (bg.EndsWith(".lvb") ? "" : ".lvb"));
        }
    }
}
var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
var errors = 0;
var missing = 0;
var layers = 0;
var lights = new Dictionary<uint, int>();
var v7Lights = 0;
var festivals = new HashSet<TerritorySceneFile.Festival>();
void Visit(string path)
{
    if (!seen.Add(path)) return;
    try
    {
        var bytes = Read(path) ?? throw new FileNotFoundException(path);
        var scene = new TerritorySceneFile(bytes);
        if (args.Length > 1)
        {
            foreach (var filter in scene.Filters) Console.WriteLine($"  FILTER {filter.Key} territory={filter.TerritoryId} content={filter.ContentId}");
            foreach (var layer in scene.Layers.Where(l => l.FilterKeys.Length != 0).Take(3))
                Console.WriteLine($"  LAYER {layer.Id} {layer.Name} op={layer.FilterOperation} keys={string.Join(',', layer.FilterKeys)}");
        }
        layers += scene.Layers.Count;
        foreach (var layer in scene.Layers)
        {
            foreach (var light in layer.Instances.Select(i => i.Light).OfType<SceneLight>())
            {
                lights[light.Type] = lights.GetValueOrDefault(light.Type) + 1;
                if (light.Dawntrail != null) v7Lights++;
                if (!float.IsFinite(light.Range) || !float.IsFinite(light.HdrIntensity))
                    throw new InvalidDataException("Invalid light range or intensity.");
            }
            if (layer.Festival.Id != 0) festivals.Add(layer.Festival);
            foreach (var item in layer.Instances.Where(i => i.Type == 6 && i.Path.Length != 0)) Visit(item.Path);
        }
        foreach (var resource in scene.LayerResources) Visit(resource);
        if (path.EndsWith(".lvb")) Console.WriteLine($"{path}: layers={scene.Layers.Count}, resources={scene.LayerResources.Count}, filters={scene.Filters.Count}, terrain={scene.TerrainPath}");
    }
    catch (FileNotFoundException) { missing++; Console.WriteLine($"MISSING {path}"); }
    catch (Exception e) { errors++; Console.WriteLine($"ERROR {path}: {e.Message}"); }
}
foreach (var path in paths.Distinct()) Visit(path);
Console.WriteLine($"Scenes={seen.Count}, layers={layers}, festival phases={festivals.Count}, missing={missing}, parse errors={errors}");
Console.WriteLine($"Lights={string.Join(", ", lights.OrderBy(p => p.Key).Select(p => $"type {p.Key}: {p.Value}"))}; V700={v7Lights}");
if (seen.Count == 0 || errors != 0 || missing != 0) Environment.ExitCode = 1;
