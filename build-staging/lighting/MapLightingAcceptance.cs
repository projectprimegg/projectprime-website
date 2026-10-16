using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;
using MphRead.Mods.Render;

namespace MphRead.Mods.MapEditor;

public static class MapLightingAcceptance
{
    public static void Append(List<StudioAcceptanceCheck> checks, MapDefinition definition)
    {
        Check(checks, "Real-time lighting validates", () =>
        {
            var result = new MapValidationResult();
            MapLightingValidator.Validate(definition, result);
            int errors = result.Diagnostics.Count(item => item.Severity == MapDiagnosticSeverity.Error);
            return (errors == 0, $"{definition.Lights.Count} light(s), {errors} validation error(s)");
        });

        Check(checks, "Point and spot lights pack into bounded runtime uniforms", () =>
        {
            MapLightingUniformPacket first = MapLightingRuntime.Pack(definition.Lighting, definition.Lights, 0);
            MapLightingUniformPacket animated = MapLightingRuntime.Pack(definition.Lighting, definition.Lights, .25f);
            bool arrays = first.PositionRange.Length == MapLightingRuntime.MaxRuntimeLights
                && first.ColorIntensity.Length == MapLightingRuntime.MaxRuntimeLights
                && first.DirectionOuter.Length == MapLightingRuntime.MaxRuntimeLights
                && first.Parameters.Length == MapLightingRuntime.MaxRuntimeLights;
            bool finite = first.PositionRange.Concat(first.ColorIntensity)
                .Concat(first.DirectionOuter).Concat(first.Parameters)
                .SelectMany(values => values).All(float.IsFinite);
            bool types = first.Count >= 2 && first.Parameters[0][0] == 0 && first.Parameters[1][0] == 1;
            bool pulse = first.ColorIntensity[1][3] != animated.ColorIntensity[1][3];
            return (arrays && finite && types && pulse,
                $"active={first.Count}, bounded={arrays}, finite={finite}, point/spot={types}, animated={pulse}");
        });

        Check(checks, "OpenGL lighting compositor contains real-time light contracts", () =>
        {
            string shader = GraphicsPipelineShader.FragmentSource;
            string[] required =
            {
                "dynamic_light_count", "dynamic_light_pos_range", "dynamic_light_color_intensity",
                "dynamic_light_direction_outer", "dynamic_light_parameters", "apply_dynamic_lighting"
            };
            string[] missing = required.Where(name => !shader.Contains(name, StringComparison.Ordinal)).ToArray();
            return (missing.Length == 0,
                missing.Length == 0 ? "Depth-reconstructed point/spot lighting shader contract is present."
                    : "Missing shader contracts: " + String.Join(", ", missing));
        });

        Check(checks, "AI Map Engineer exposes controlled lighting tools", () =>
        {
            string[] required = { "inspect_lighting", "add_light", "update_light", "set_lighting_globals", "create_lighting_rig" };
            string[] available = AiMapEngineerSession.Tools.Select(tool => tool.Name).ToArray();
            string[] missing = required.Except(available, StringComparer.Ordinal).ToArray();
            return (missing.Length == 0,
                missing.Length == 0 ? $"{required.Length} lighting tools present"
                    : "Missing AI lighting tools: " + String.Join(", ", missing));
        });
    }

    private static void Check(List<StudioAcceptanceCheck> checks, string name,
        Func<(bool Passed, string Detail)> action)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            (bool passed, string detail) = action();
            checks.Add(new(name, passed, detail, watch.Elapsed.TotalMilliseconds));
        }
        catch (Exception ex)
        {
            checks.Add(new(name, false, ex.GetType().Name + ": " + ex.Message,
                watch.Elapsed.TotalMilliseconds));
        }
    }
}
