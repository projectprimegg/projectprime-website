using System;
using System.Linq;
using MphRead.Mods;
using MphRead.Mods.MapGen;

namespace MphRead;

public partial class Scene
{
    private MapLightingSettings _authoredLightingSettings = new();
    private MapLight[] _authoredRuntimeLights = Array.Empty<MapLight>();

    private bool HasAuthoredRuntimeLighting
        => _authoredLightingSettings.Enabled
            && (_authoredRuntimeLights.Any(light => light.Enabled)
                || _authoredLightingSettings.AmbientIntensity > 0
                || Math.Abs(_authoredLightingSettings.Exposure - 1f) > .0001f);

    /// <summary>
    /// Select the custom-map lighting recipe when a room is installed. Built-in
    /// rooms and maps without authored lights remain on the original renderer.
    /// </summary>
    internal void BindAuthoredMapLighting(string roomName)
    {
        MapDefinition? definition = null;
        try
        {
            definition = CustomRooms.Definitions.FirstOrDefault(map =>
                map.Name.Equals(roomName, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is System.IO.IOException or InvalidDataException)
        {
            Mods.DebugLog.Line("lighting", "Could not load authored lighting for " + roomName + ": " + ex.Message);
        }

        if (definition == null)
        {
            _authoredLightingSettings = new() { Enabled = false };
            _authoredRuntimeLights = Array.Empty<MapLight>();
        }
        else
        {
            _authoredLightingSettings = definition.Lighting ?? new();
            _authoredRuntimeLights = definition.Lights?.Where(light => light != null).ToArray()
                ?? Array.Empty<MapLight>();
        }
        RenderOptions.CustomMapLightingActive = HasAuthoredRuntimeLighting;
    }

    private MapLightingUniformPacket CurrentAuthoredLighting()
        => MapLightingRuntime.Pack(_authoredLightingSettings, _authoredRuntimeLights, _elapsedTime);
}
