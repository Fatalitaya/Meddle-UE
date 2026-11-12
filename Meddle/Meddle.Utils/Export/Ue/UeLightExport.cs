using System.Numerics;
using System.Text.Json;
using Meddle.Unreal;
using SharpGLTF.Schema2;

namespace Meddle.Utils.Export.Ue;

public static class UeLightExport
{
    public static LevelLight[] Read(ModelRoot scene) => scene.LogicalNodes.Where(n => n.PunctualLight != null).Select(node =>
    {
        var light = node.PunctualLight!;
        var extras = node.Extras;
        var type = light.LightType switch
        {
            PunctualLightType.Directional => LevelLightType.Directional,
            PunctualLightType.Spot => LevelLightType.Spot,
            _ => LevelLightType.Point
        };
        return new LevelLight(node.Name ?? "Light", type, Coordinates.Position(node.WorldMatrix.Translation),
            Coordinates.Direction(Vector3.Normalize(Vector3.TransformNormal(-Vector3.UnitZ, node.WorldMatrix))),
            light.Color, light.Intensity, type == LevelLightType.Directional ? 0 : light.Range * 100,
            type == LevelLightType.Spot ? light.InnerConeAngle * 180 / MathF.PI : 0,
            type == LevelLightType.Spot ? light.OuterConeAngle * 180 / MathF.PI : 0,
            (extras?["ObjectShadow"]?.GetValue<bool>() ?? false) || (extras?["CharaShadow"]?.GetValue<bool>() ?? false),
            extras?["Reflection"]?.GetValue<bool>() ?? true, extras?.ToJsonString());
    }).ToArray();

    public static void WriteMetadata(string mapPath, LevelLight[] lights) => File.WriteAllText(
        Path.ChangeExtension(mapPath, ".lights.json"), JsonSerializer.Serialize(lights,
            new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
}
