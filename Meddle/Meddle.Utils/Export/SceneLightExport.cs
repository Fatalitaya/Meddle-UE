using System.Numerics;
using System.Text.Json.Nodes;
using SharpGLTF.Scenes;

namespace Meddle.Utils.Export;

public static class SceneLightExport
{
    /// <summary>Game lights face local +Z; glTF punctual lights face local -Z.</summary>
    public static NodeBuilder Add(SceneBuilder scene, string name, Matrix4x4 world, uint type,
        Vector3 linearColor, float multiplier, float range, float coneDegrees, float falloffDegrees,
        JsonNode? extras = null)
    {
        var peak = MathF.Max(linearColor.X, MathF.Max(linearColor.Y, linearColor.Z));
        var color = peak > 0 ? linearColor / peak : Vector3.Zero;
        var intensity = MathF.Max(0, peak * multiplier);
        LightBuilder light;
        if (type is 1 or 6) light = new LightBuilder.Directional();
        else if (type == 3)
        {
            var inner = Math.Clamp(coneDegrees * MathF.PI / 180, 0, MathF.PI / 2 - .0002f);
            var outer = Math.Clamp((coneDegrees + falloffDegrees) * MathF.PI / 180, inner + .0001f, MathF.PI / 2);
            // Keep the outer angle explicit for glTF importers that mishandle its default.
            if (MathF.Abs(outer - MathF.PI / 4) < .00001f) outer += .00001f;
            light = new LightBuilder.Spot { InnerConeAngle = inner, OuterConeAngle = outer,
                Range = range > 0 ? range : .001f };
        }
        else light = new LightBuilder.Point { Range = range > 0 ? range : .001f };
        light.Color = color;
        light.Intensity = intensity;
        light.Name = name;
        light.Extras = extras?.DeepClone();
        var node = new NodeBuilder(name) { Extras = extras?.DeepClone() };
        // Scale does not affect punctual lights. The file range was already evaluated by the client formula.
        if (!Matrix4x4.Decompose(world, out _, out var rotation, out var position))
            throw new InvalidDataException($"Cannot decompose light transform: {name}");
        node.SetLocalTransform(Matrix4x4.CreateRotationY(MathF.PI) * Matrix4x4.CreateFromQuaternion(rotation)
            * Matrix4x4.CreateTranslation(position), true);
        scene.AddLight(light, node);
        return node;
    }
}
