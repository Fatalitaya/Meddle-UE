using System.Numerics;

namespace Meddle.Formats.Files;

/// <summary>LVB/LGB/SGB light payload. Colours and distances follow the client, not photometric units.</summary>
public sealed record SceneLight(uint Type, float Attenuation, float RangeRate, uint PointLightType,
    float FalloffAngle, float ConeAngle, string TexturePath, uint ColorRgba, float HdrIntensity,
    bool FollowsDirectionalLight, bool SpecularEnabled, bool ObjectShadowEnabled, bool CharacterShadowEnabled,
    float ShadowNear, Vector2 PlaneAngles)
{
    public ushort MergeGroupId { get; init; }
    public SceneLightV7? Dawntrail { get; init; }
    // Client 2026.11.03: 0x140799730 and 0x14026ac30. See docs/SCENE_LIGHTS.md.
    public Vector3 LinearColor
    {
        get
        {
            var color = new Vector3(ColorRgba & 255, (ColorRgba >> 8) & 255, (ColorRgba >> 16) & 255)
                * (HdrIntensity / 255f);
            return color * color;
        }
    }
    public uint FalloffType => Attenuation <= 1.2f ? 0u : Attenuation <= 2.2f ? 1u : 2u;
    public float FalloffFactor => MathF.Max(RangeRate, 1);
    public float Range => RangeWithMultiplier(1);

    public float RangeWithMultiplier(float multiplier)
    {
        if (Type is 1 or 6) return 10000;
        var color = LinearColor;
        var value = MathF.Max(color.X, MathF.Max(color.Y, color.Z)) * multiplier / (FalloffFactor * (5f / 255f));
        return FalloffType switch { 1 => MathF.Sqrt(value), 2 => MathF.Pow(value, 1f / 3), _ => value };
    }
}

public sealed record SceneLightV7(float LightSelect, float ShadowDepthNear, float ShadowDepthFar,
    float ShadowStart, float ShadowEnd, float ShadowBias, float Fade, float FadeLength,
    bool EnableSsao, ushort MergeGroupId, byte Flag55, byte Flag56, byte Flag5B);
