using System;
using System.Collections.Generic;
using System.Linq;

namespace MphRead.Mods.MapGen;

public enum MapLightKind
{
    Point,
    Spot
}

/// <summary>
/// Global controls for authored real-time lights. These are applied by the
/// OpenGL depth-aware lighting compositor at runtime; they are not baked into
/// textures or vertex colors.
/// </summary>
public sealed class MapLightingSettings
{
    public bool Enabled { get; set; } = true;
    public int[] AmbientColor { get; set; } = new[] { 0, 0, 0 };
    public float AmbientIntensity { get; set; }
    public float Exposure { get; set; } = 1f;
    public float DiffuseStrength { get; set; } = 1f;
    public float SpecularStrength { get; set; } = .15f;
    public int RuntimeLightLimit { get; set; } = MapLightingRuntime.MaxRuntimeLights;
}

/// <summary>One editable, serializable real-time point or spot light.</summary>
public sealed class MapLight
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Label { get; set; } = "Real-time light";
    public bool Enabled { get; set; } = true;
    public MapLightKind Kind { get; set; } = MapLightKind.Point;
    public float[] Position { get; set; } = new float[3];
    public float[] Direction { get; set; } = new[] { 0f, -1f, 0f };
    public int[] Color { get; set; } = new[] { 255, 224, 192 };
    public float Intensity { get; set; } = 1f;
    public float Range { get; set; } = 18f;
    public float InnerConeDegrees { get; set; } = 24f;
    public float OuterConeDegrees { get; set; } = 42f;
    public float Falloff { get; set; } = 2f;
    public float PulseAmplitude { get; set; }
    public float PulseSpeed { get; set; }
    public float Phase { get; set; }
}

public sealed record MapLightingUniformPacket(
    int Count,
    float[] Ambient,
    float[] Settings,
    float[][] PositionRange,
    float[][] ColorIntensity,
    float[][] DirectionOuter,
    float[][] Parameters);

/// <summary>
/// Deterministic CPU preparation shared by the renderer, Studio tests and AI
/// tools. At most sixteen authored lights are uploaded to the legacy OpenGL
/// shader so the feature stays bounded on OpenGL 2.1 and GLES hardware.
/// </summary>
public static class MapLightingRuntime
{
    public const int MaxRuntimeLights = 16;
    public const int MaxAuthoredLights = 256;

    public static MapLightingUniformPacket Pack(
        MapLightingSettings? settings,
        IReadOnlyList<MapLight>? lights,
        float timeSeconds)
    {
        settings ??= new();
        lights ??= Array.Empty<MapLight>();
        int limit = Math.Clamp(settings.RuntimeLightLimit, 1, MaxRuntimeLights);
        MapLight[] selected = settings.Enabled
            ? lights.Where(light => light != null && light.Enabled)
                .Take(limit).ToArray()
            : Array.Empty<MapLight>();

        var positionRange = new float[MaxRuntimeLights][];
        var colorIntensity = new float[MaxRuntimeLights][];
        var directionOuter = new float[MaxRuntimeLights][];
        var parameters = new float[MaxRuntimeLights][];
        for (int index = 0; index < MaxRuntimeLights; index++)
        {
            positionRange[index] = new float[4];
            colorIntensity[index] = new float[4];
            directionOuter[index] = new float[4];
            parameters[index] = new float[4];
        }

        for (int index = 0; index < selected.Length; index++)
        {
            MapLight light = selected[index];
            float[] direction = Normalize(light.Direction);
            float pulse = 1f;
            if (light.PulseAmplitude > 0 && light.PulseSpeed != 0)
            {
                pulse += Math.Clamp(light.PulseAmplitude, 0, 1)
                    * MathF.Sin(light.Phase + timeSeconds * MathF.Tau * light.PulseSpeed);
            }
            float intensity = Math.Max(0, light.Intensity * pulse);
            float inner = MathF.Cos(Math.Clamp(light.InnerConeDegrees, 0, 179) * MathF.PI / 180f);
            float outer = MathF.Cos(Math.Clamp(light.OuterConeDegrees, 0, 179) * MathF.PI / 180f);

            positionRange[index] = new[]
            {
                Value(light.Position, 0), Value(light.Position, 1), Value(light.Position, 2),
                Math.Max(.01f, light.Range)
            };
            colorIntensity[index] = new[]
            {
                Channel(light.Color, 0), Channel(light.Color, 1), Channel(light.Color, 2), intensity
            };
            directionOuter[index] = new[] { direction[0], direction[1], direction[2], outer };
            parameters[index] = new[]
            {
                light.Kind == MapLightKind.Spot ? 1f : 0f,
                inner,
                Math.Clamp(light.Falloff, .25f, 8f),
                0f
            };
        }

        return new(
            selected.Length,
            new[]
            {
                Channel(settings.AmbientColor, 0), Channel(settings.AmbientColor, 1),
                Channel(settings.AmbientColor, 2), Math.Clamp(settings.AmbientIntensity, 0, 4)
            },
            new[]
            {
                Math.Clamp(settings.Exposure, .25f, 4f),
                Math.Clamp(settings.DiffuseStrength, 0, 4),
                Math.Clamp(settings.SpecularStrength, 0, 4),
                0f
            },
            positionRange, colorIntensity, directionOuter, parameters);
    }

    private static float Value(float[]? values, int index)
        => values != null && index < values.Length && float.IsFinite(values[index]) ? values[index] : 0;

    private static float Channel(int[]? values, int index)
        => values != null && index < values.Length ? Math.Clamp(values[index], 0, 255) / 255f : 0;

    private static float[] Normalize(float[]? values)
    {
        float x = Value(values ?? Array.Empty<float>(), 0);
        float y = Value(values ?? Array.Empty<float>(), 1);
        float z = Value(values ?? Array.Empty<float>(), 2);
        float length = MathF.Sqrt(x * x + y * y + z * z);
        return length > .0001f ? new[] { x / length, y / length, z / length }
            : new[] { 0f, -1f, 0f };
    }
}

public static class MapLightingValidator
{
    public static void Validate(MapDefinition definition, MapValidationResult result)
    {
        if (definition.Lighting == null || definition.Lights == null)
        {
            result.Error("FP-LIGHT-001", "Real-time lighting collections cannot be null.");
            return;
        }
        MapLightingSettings settings = definition.Lighting;
        if (!Color(settings.AmbientColor)
            || !Finite(settings.AmbientIntensity, 0, 4)
            || !Finite(settings.Exposure, .25f, 4)
            || !Finite(settings.DiffuseStrength, 0, 4)
            || !Finite(settings.SpecularStrength, 0, 4)
            || settings.RuntimeLightLimit is < 1 or > MapLightingRuntime.MaxRuntimeLights)
        {
            result.Error("FP-LIGHT-001",
                "Lighting globals require RGB 0-255, ambient/diffuse/specular 0-4, exposure 0.25-4 and a runtime limit of 1-16.");
        }
        if (definition.Lights.Count > MapLightingRuntime.MaxAuthoredLights)
            result.Error("FP-LIGHT-002", $"A map can author at most {MapLightingRuntime.MaxAuthoredLights} real-time lights.");

        var ids = new HashSet<Guid>();
        foreach (MapLight light in definition.Lights)
        {
            if (light == null)
            {
                result.Error("FP-LIGHT-001", "Null real-time light.");
                continue;
            }
            if (light.Id == Guid.Empty || !ids.Add(light.Id))
                result.Error("FP-LIGHT-001", "Real-time lights require unique nonempty IDs.", light.Id);
            if (String.IsNullOrWhiteSpace(light.Label) || light.Label.Length > 128)
                result.Error("FP-LIGHT-001", "Light labels must contain 1-128 characters.", light.Id);
            if (!Vector(light.Position) || !Vector(light.Direction)
                || light.Direction.All(component => Math.Abs(component) < .0001f))
                result.Error("FP-LIGHT-001", "Light position and direction must be finite triples; direction cannot be zero.", light.Id);
            if (!Color(light.Color)
                || !Finite(light.Intensity, 0, 32)
                || !Finite(light.Range, .01f, 4096)
                || !Finite(light.Falloff, .25f, 8)
                || !Finite(light.PulseAmplitude, 0, 1)
                || !Finite(light.PulseSpeed, -20, 20)
                || !float.IsFinite(light.Phase))
                result.Error("FP-LIGHT-001", "Light color, intensity, range, falloff or animation is outside the supported range.", light.Id);
            if (!Finite(light.InnerConeDegrees, 0, 179)
                || !Finite(light.OuterConeDegrees, 0, 179)
                || light.InnerConeDegrees > light.OuterConeDegrees)
                result.Error("FP-LIGHT-001", "Spot-light inner cone must be no wider than its outer cone; both must be 0-179 degrees.", light.Id);
        }
    }

    private static bool Vector(float[]? values)
        => values?.Length == 3 && values.All(float.IsFinite)
            && values.All(value => Math.Abs(value) < 524288);

    private static bool Color(int[]? values)
        => values?.Length == 3 && values.All(value => value is >= 0 and <= 255);

    private static bool Finite(float value, float minimum, float maximum)
        => float.IsFinite(value) && value >= minimum && value <= maximum;
}
