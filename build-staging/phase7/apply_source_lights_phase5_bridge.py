from pathlib import Path
import sys


def replace_once(path: Path, before: str, after: str, label: str) -> None:
    text = path.read_text(encoding="utf-8")
    count = text.count(before)
    if count != 1:
        raise RuntimeError(f"{label}: expected one anchor, found {count} in {path}")
    path.write_text(text.replace(before, after, 1), encoding="utf-8", newline="\n")


if len(sys.argv) != 2:
    raise SystemExit("usage: apply_source_lights_phase5_bridge.py <project-root>")
root = Path(sys.argv[1]).resolve()
path = root / "src/MphRead/Mods/Render/MapRuntimeLighting.cs"
replace_once(path,
'''using System;
using System.IO;
''',
'''using System;
using System.Collections.Generic;
using System.IO;
''',
"source-light collection import")
replace_once(path,
'''            _authoredLightingSettings = definition.Lighting ?? new();
            _authoredRuntimeLights = definition.Lights?.Where(light => light != null).ToArray()
                ?? Array.Empty<MapLight>();
''',
'''            _authoredLightingSettings = definition.Lighting ?? new();
            List<MapLight> combined = definition.Lights?
                .Where(light => light != null).ToList() ?? new List<MapLight>();
            UniversalNativeMapData? native = definition.NativeFidelity;
            if (native is { UseSourceLights: true }
                && native.LightingMode is UniversalLightingMode.SourceBakedAndRealtime
                    or UniversalLightingMode.RealtimeOnly)
            {
                var ids = combined.Select(light => light.Id).ToHashSet();
                foreach (UniversalRealtimeLight source in native.SourceLights)
                {
                    if (combined.Count >= MapLightingRuntime.MaxAuthoredLights) break;
                    if (source.Id == Guid.Empty || !ids.Add(source.Id)) continue;
                    static float Value(float[]? values, int index, float fallback = 0)
                        => values != null && index < values.Length && float.IsFinite(values[index])
                            ? values[index] : fallback;
                    static int Channel(float[]? values, int index)
                        => Math.Clamp((int)MathF.Round(Value(values, index, 1f) * 255f), 0, 255);
                    combined.Add(new MapLight
                    {
                        Id = source.Id,
                        Label = String.IsNullOrWhiteSpace(source.Name)
                            ? "Imported source light" : source.Name,
                        Kind = source.Type.Equals("Spot", StringComparison.OrdinalIgnoreCase)
                            ? MapLightKind.Spot : MapLightKind.Point,
                        Position = new[]
                        {
                            Value(source.Position, 0), Value(source.Position, 1),
                            Value(source.Position, 2)
                        },
                        Direction = new[]
                        {
                            Value(source.Direction, 0), Value(source.Direction, 1, -1f),
                            Value(source.Direction, 2)
                        },
                        Color = new[]
                        {
                            Channel(source.Color, 0), Channel(source.Color, 1),
                            Channel(source.Color, 2)
                        },
                        Intensity = Math.Clamp(source.Intensity, 0, 32),
                        Range = Math.Clamp(source.Range, .01f, 4096),
                        InnerConeDegrees = Math.Clamp(source.InnerConeDegrees, 0, 179),
                        OuterConeDegrees = Math.Clamp(
                            Math.Max(source.InnerConeDegrees, source.OuterConeDegrees), 0, 179),
                        Falloff = Math.Clamp(source.Falloff, .25f, 8)
                    });
                }
            }
            _authoredRuntimeLights = combined.ToArray();
''',
"source-light runtime bridge")
print("Imported source lights bridged into Phase 5 real-time lighting")
