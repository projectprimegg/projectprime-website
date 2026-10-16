using System;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.MapEditor;

public sealed partial class AiMapEngineerSession
{
    private static AiMapEngineerResult InspectLighting(MapDocument document)
    {
        MapDefinition map = document.CaptureBuildSnapshot().CreateDefinition();
        MapLightingUniformPacket packet = MapLightingRuntime.Pack(map.Lighting, map.Lights, 0);
        return AiMapEngineerResult.Ok("inspect_lighting",
            map.Lights.Count == 0
                ? "This map has no local real-time lights yet."
                : $"The map has {map.Lights.Count} authored light(s); {packet.Count} would be active at runtime.",
            new
            {
                map.Lighting,
                authored = map.Lights.Select((light, index) => new
                {
                    order = index + 1,
                    light.Id,
                    light.Label,
                    type = light.Kind.ToString(),
                    light.Enabled,
                    light.Position,
                    light.Direction,
                    light.Color,
                    light.Intensity,
                    light.Range,
                    light.Falloff,
                    light.InnerConeDegrees,
                    light.OuterConeDegrees,
                    light.PulseAmplitude,
                    light.PulseSpeed,
                    light.Phase
                }).ToArray(),
                activeRuntimeLights = packet.Count,
                maximumRuntimeLights = MapLightingRuntime.MaxRuntimeLights
            });
    }

    private static AiMapEngineerResult AddLight(MapDocument document, JsonObject? args)
    {
        MapLightKind kind = Enum.TryParse(ReadString(args, "kind"), true, out MapLightKind parsed)
            ? parsed : MapLightKind.Point;
        float[] position = OptionalVector(args, "position", new float[3]);
        float[] direction = OptionalVector(args, "direction", new[] { 0f, -1f, 0f });
        int[] color = OptionalColor(args, "color", new[] { 255, 224, 192 });
        Guid id = Guid.NewGuid();
        string label = ReadString(args, "label").Trim();
        if (label.Length == 0) label = kind == MapLightKind.Point ? "AI point light" : "AI spot light";
        float intensity = OptionalNumber(args, "intensity", kind == MapLightKind.Point ? 1.2f : 1.6f);
        float range = OptionalNumber(args, "range", kind == MapLightKind.Point ? 18f : 28f);
        float falloff = OptionalNumber(args, "falloff", 2f);
        float inner = OptionalNumber(args, "innerConeDegrees", 24f);
        float outer = OptionalNumber(args, "outerConeDegrees", 42f);
        float pulseAmplitude = OptionalNumber(args, "pulseAmplitude", 0f);
        float pulseSpeed = OptionalNumber(args, "pulseSpeed", 0f);
        float phase = OptionalNumber(args, "phase", 0f);
        document.Edit("AI add real-time light", map =>
        {
            map.Lighting ??= new();
            map.Lights ??= new();
            map.Lights.Add(new MapLight
            {
                Id = id,
                Label = label,
                Kind = kind,
                Position = position,
                Direction = direction,
                Color = color,
                Intensity = intensity,
                Range = range,
                Falloff = falloff,
                InnerConeDegrees = inner,
                OuterConeDegrees = outer,
                PulseAmplitude = pulseAmplitude,
                PulseSpeed = pulseSpeed,
                Phase = phase
            });
        }, MapChangeDomain.Entity | MapChangeDomain.Environment);
        return AiMapEngineerResult.Ok("add_light", $"Added {kind.ToString().ToLowerInvariant()} light '{label}'.",
            new { id, label, kind = kind.ToString(), position }, changed: true);
    }

    private static AiMapEngineerResult UpdateLight(MapDocument document, JsonObject? args)
    {
        string raw = ReadString(args, "id");
        if (!Guid.TryParse(raw, out Guid id))
            return AiMapEngineerResult.Fail("update_light", "A valid light id is required.");
        MapLight? current = document.Project.Definition.Lights.FirstOrDefault(light => light.Id == id);
        if (current == null)
            return AiMapEngineerResult.Fail("update_light", "The requested real-time light was not found.");

        document.Edit("AI update real-time light", map =>
        {
            MapLight light = map.Lights.Single(item => item.Id == id);
            if (args?["label"] != null) light.Label = ReadString(args, "label").Trim();
            if (args?["enabled"] != null) light.Enabled = ReadBool(args, "enabled");
            if (args?["kind"] != null && Enum.TryParse(ReadString(args, "kind"), true, out MapLightKind kind)) light.Kind = kind;
            if (args?["position"] != null) light.Position = ReadVector(args, "position");
            if (args?["direction"] != null) light.Direction = ReadVector(args, "direction");
            if (args?["color"] != null) light.Color = RequiredColor(args, "color");
            Assign(args, "intensity", value => light.Intensity = value);
            Assign(args, "range", value => light.Range = value);
            Assign(args, "falloff", value => light.Falloff = value);
            Assign(args, "innerConeDegrees", value => light.InnerConeDegrees = value);
            Assign(args, "outerConeDegrees", value => light.OuterConeDegrees = value);
            Assign(args, "pulseAmplitude", value => light.PulseAmplitude = value);
            Assign(args, "pulseSpeed", value => light.PulseSpeed = value);
            Assign(args, "phase", value => light.Phase = value);
        }, MapChangeDomain.Entity | MapChangeDomain.Environment);
        return AiMapEngineerResult.Ok("update_light", $"Updated real-time light '{current.Label}'.",
            new { id }, changed: true);
    }

    private static AiMapEngineerResult SetLightingGlobals(MapDocument document, JsonObject? args)
    {
        document.Edit("AI update lighting globals", map =>
        {
            map.Lighting ??= new();
            MapLightingSettings lighting = map.Lighting;
            if (args?["enabled"] != null) lighting.Enabled = ReadBool(args, "enabled");
            if (args?["ambientColor"] != null) lighting.AmbientColor = RequiredColor(args, "ambientColor");
            Assign(args, "ambientIntensity", value => lighting.AmbientIntensity = value);
            Assign(args, "exposure", value => lighting.Exposure = value);
            Assign(args, "diffuseStrength", value => lighting.DiffuseStrength = value);
            Assign(args, "specularStrength", value => lighting.SpecularStrength = value);
            if (args?["runtimeLightLimit"] != null)
                lighting.RuntimeLightLimit = args["runtimeLightLimit"]!.GetValue<int>();
        }, MapChangeDomain.Environment);
        return AiMapEngineerResult.Ok("set_lighting_globals", "Updated the map's real-time lighting globals.",
            new { document.Project.Definition.Lighting }, changed: true);
    }

    private static AiMapEngineerResult CreateLightingRig(MapDocument document, JsonObject? args)
    {
        string preset = ReadString(args, "preset").Trim().ToLowerInvariant();
        if (preset.Length == 0) preset = "three-point";
        float[] centerArray = OptionalVector(args, "center", new float[3]);
        var center = new System.Numerics.Vector3(centerArray[0], centerArray[1], centerArray[2]);
        document.Edit("AI create " + preset + " lighting rig", map =>
        {
            map.Lighting ??= new();
            map.Lighting.Enabled = true;
            map.Lights ??= new();
            if (preset is "neon" or "neon-arena")
            {
                map.Lighting.AmbientColor = new[] { 16, 20, 36 };
                map.Lighting.AmbientIntensity = .22f;
                map.Lights.Add(AiRigLight("Neon cyan", center + new System.Numerics.Vector3(-12, 7, -8), new[] { 40, 210, 255 }, 2.1f, 30));
                map.Lights.Add(AiRigLight("Neon magenta", center + new System.Numerics.Vector3(12, 6, 7), new[] { 255, 45, 210 }, 2.1f, 30));
                map.Lights.Add(AiRigLight("Neon gold", center + new System.Numerics.Vector3(0, 10, 12), new[] { 255, 170, 35 }, 1.5f, 26));
                map.Lights.Add(AiRigLight("Arena fill", center + new System.Numerics.Vector3(0, 14, 0), new[] { 120, 150, 255 }, 1.1f, 38));
            }
            else
            {
                map.Lighting.AmbientColor = new[] { 22, 24, 30 };
                map.Lighting.AmbientIntensity = .14f;
                map.Lights.Add(AiRigLight("Key light", center + new System.Numerics.Vector3(-10, 10, -8), new[] { 255, 224, 190 }, 2f, 34));
                map.Lights.Add(AiRigLight("Fill light", center + new System.Numerics.Vector3(10, 6, -2), new[] { 145, 180, 255 }, 1.1f, 30));
                map.Lights.Add(AiRigLight("Rim light", center + new System.Numerics.Vector3(0, 8, 12), new[] { 255, 120, 80 }, 1.35f, 28));
            }
        }, MapChangeDomain.Entity | MapChangeDomain.Environment);
        return AiMapEngineerResult.Ok("create_lighting_rig", $"Created the {preset} real-time lighting rig.",
            new { preset, center = centerArray }, changed: true);
    }

    private static MapLight AiRigLight(string label, System.Numerics.Vector3 position,
        int[] color, float intensity, float range) => new()
    {
        Label = label,
        Kind = MapLightKind.Point,
        Position = new[] { position.X, position.Y, position.Z },
        Color = color,
        Intensity = intensity,
        Range = range,
        Falloff = 2f
    };

    private static float OptionalNumber(JsonObject? args, string name, float fallback)
    {
        if (args?[name] == null) return fallback;
        float value = args[name]!.GetValue<float>();
        if (!float.IsFinite(value)) throw new ArgumentOutOfRangeException(name, "Value must be finite.");
        return value;
    }

    private static void Assign(JsonObject? args, string name, Action<float> apply)
    {
        if (args?[name] != null) apply(OptionalNumber(args, name, 0));
    }

    private static float[] OptionalVector(JsonObject? args, string name, float[] fallback)
        => args?[name] == null ? (float[])fallback.Clone() : ReadVector(args, name);

    private static int[] OptionalColor(JsonObject? args, string name, int[] fallback)
        => args?[name] == null ? (int[])fallback.Clone() : RequiredColor(args, name);

    private static int[] RequiredColor(JsonObject? args, string name)
    {
        if (args?[name] is not JsonArray array || array.Count != 3)
            throw new ArgumentException($"{name} must be a three-number RGB array.");
        var result = new int[3];
        for (int index = 0; index < 3; index++)
        {
            result[index] = array[index]?.GetValue<int>()
                ?? throw new ArgumentException($"{name}[{index}] is missing.");
            if (result[index] is < 0 or > 255)
                throw new ArgumentOutOfRangeException(name, "RGB channels must be 0-255.");
        }
        return result;
    }
}
