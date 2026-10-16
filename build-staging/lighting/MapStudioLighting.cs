using System;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Launcher.Gui;

internal sealed partial class MapStudioScreen
{
    private void LightingInspector()
    {
        _inspector.Children.Clear();
        if (_document == null) return;
        MapDefinition definition = _document.Project.Definition;
        MapLightingSettings lighting = definition.Lighting ?? new();

        _inspector.Children.Add(PrimeChrome.Eyebrow("REAL-TIME MAP LIGHTING"));
        _inspector.Children.Add(PrimeChrome.Text(
            "Point and spot lights are evaluated every frame from the depth buffer. They illuminate moving players, weapons and map surfaces and are not baked into textures.",
            PrimeTypography.BodySmall, PrimeTheme.TextSecondaryBrush));

        var enabled = new CheckBox { Content = "Enable authored real-time lighting", IsChecked = lighting.Enabled };
        var ambientColor = new TextBox { Text = Join(lighting.AmbientColor) };
        var ambientIntensity = new TextBox { Text = F(lighting.AmbientIntensity) };
        var exposure = new TextBox { Text = F(lighting.Exposure) };
        var diffuse = new TextBox { Text = F(lighting.DiffuseStrength) };
        var specular = new TextBox { Text = F(lighting.SpecularStrength) };
        var limit = new TextBox { Text = lighting.RuntimeLightLimit.ToString(CultureInfo.InvariantCulture) };
        _inspector.Children.Add(enabled);
        AddLightingField("Ambient RGB (0-255)", ambientColor,
            "Adds a map-wide real-time color without changing baked textures.");
        AddLightingField("Ambient intensity (0-4)", ambientIntensity,
            "Zero preserves the native map ambient result. Higher values add colored fill light.");
        AddLightingField("Exposure (0.25-4)", exposure,
            "Final brightness applied by the real-time lighting compositor.");
        AddLightingField("Diffuse strength (0-4)", diffuse,
            "How strongly surface direction affects point and spot illumination.");
        AddLightingField("Specular strength (0-4)", specular,
            "Depth-normal highlight strength from authored lights.");
        AddLightingField("Maximum active lights (1-16)", limit,
            "The first enabled lights in the list are uploaded, up to this bounded OpenGL limit.");
        AddButton(_inspector, "APPLY GLOBAL LIGHTING", () =>
        {
            try
            {
                int[] ambient = ParseColor(ambientColor.Text);
                float ambientValue = Number(ambientIntensity.Text ?? "0");
                float exposureValue = Number(exposure.Text ?? "1");
                float diffuseValue = Number(diffuse.Text ?? "1");
                float specularValue = Number(specular.Text ?? "0.15");
                int lightLimit = Int32.Parse(limit.Text ?? "16", CultureInfo.InvariantCulture);
                _document.Edit("Edit real-time lighting globals", map =>
                {
                    map.Lighting ??= new();
                    map.Lighting.Enabled = enabled.IsChecked == true;
                    map.Lighting.AmbientColor = ambient;
                    map.Lighting.AmbientIntensity = ambientValue;
                    map.Lighting.Exposure = exposureValue;
                    map.Lighting.DiffuseStrength = diffuseValue;
                    map.Lighting.SpecularStrength = specularValue;
                    map.Lighting.RuntimeLightLimit = lightLimit;
                }, MapChangeDomain.Environment);
                LightingInspector();
            }
            catch (Exception ex) { Failure(ex); }
        });

        var creation = new WrapPanel { Margin = new Thickness(0, 8, 0, 4) };
        AddButton(creation, "ADD POINT LIGHT", () => AddAuthoredLight(MapLightKind.Point));
        AddButton(creation, "ADD SPOT LIGHT", () => AddAuthoredLight(MapLightKind.Spot));
        AddButton(creation, "THREE-POINT RIG", () => AddLightingRig(neon: false));
        AddButton(creation, "NEON ARENA RIG", () => AddLightingRig(neon: true));
        AddButton(creation, "PLAYTEST LIGHTING", PlaytestInspector);
        _inspector.Children.Add(creation);

        _inspector.Children.Add(PrimeChrome.Text(
            $"AUTHORED LIGHTS · {definition.Lights?.Count ?? 0} / {MapLightingRuntime.MaxAuthoredLights}",
            PrimeTypography.DataSmall, PrimeTheme.PrimaryBrush, data: true));

        if (definition.Lights == null || definition.Lights.Count == 0)
        {
            _inspector.Children.Add(PrimeChrome.Text(
                "No local lights yet. Add a point light for an omnidirectional source or a spot light for a cone-shaped source.",
                PrimeTypography.BodySmall, PrimeTheme.TextSecondaryBrush));
            return;
        }

        for (int index = 0; index < definition.Lights.Count; index++)
        {
            MapLight light = definition.Lights[index];
            Guid id = light.Id;
            int authoredIndex = index;
            var content = new StackPanel { Spacing = 5 };
            content.Children.Add(PrimeChrome.Columns("*,Auto",
                PrimeChrome.Title($"{index + 1}. {light.Label}"),
                new PrimeBadge(light.Kind.ToString().ToUpperInvariant())));

            var label = new TextBox { Text = light.Label };
            var kind = new ComboBox { ItemsSource = Enum.GetValues<MapLightKind>(), SelectedItem = light.Kind };
            var lightEnabled = new CheckBox { Content = "Enabled", IsChecked = light.Enabled };
            var position = new TextBox { Text = Join(light.Position) };
            var direction = new TextBox { Text = Join(light.Direction) };
            var color = new TextBox { Text = Join(light.Color) };
            var intensity = new TextBox { Text = F(light.Intensity) };
            var range = new TextBox { Text = F(light.Range) };
            var falloff = new TextBox { Text = F(light.Falloff) };
            var inner = new TextBox { Text = F(light.InnerConeDegrees) };
            var outer = new TextBox { Text = F(light.OuterConeDegrees) };
            var pulseAmplitude = new TextBox { Text = F(light.PulseAmplitude) };
            var pulseSpeed = new TextBox { Text = F(light.PulseSpeed) };
            var phase = new TextBox { Text = F(light.Phase) };

            AddLightingField(content, "Label", label, "Name shown in the scene hierarchy and lighting inspector.");
            AddLightingField(content, "Type", kind, "Point lights shine in every direction. Spot lights use direction and cone angles.");
            content.Children.Add(lightEnabled);
            AddLightingField(content, "Position X, Y, Z", position, "World-space center of the light.");
            AddLightingField(content, "Direction X, Y, Z", direction, "Spot-light forward direction. It is normalized automatically.");
            AddLightingField(content, "RGB color (0-255)", color, "Full 24-bit authored light color.");
            AddLightingField(content, "Intensity (0-32)", intensity, "Brightness before distance attenuation.");
            AddLightingField(content, "Range (0.01-4096)", range, "Maximum world-space distance affected by this light.");
            AddLightingField(content, "Falloff (0.25-8)", falloff, "Higher values make the light fade more sharply toward its range boundary.");
            AddLightingField(content, "Inner cone degrees", inner, "Spot light is fully bright inside this cone.");
            AddLightingField(content, "Outer cone degrees", outer, "Spot light fades to zero at this wider cone.");
            AddLightingField(content, "Pulse amplitude (0-1)", pulseAmplitude, "Zero is steady. One allows a full intensity pulse.");
            AddLightingField(content, "Pulse speed cycles/second (-20 to 20)", pulseSpeed, "Positive and negative values animate in opposite phase directions.");
            AddLightingField(content, "Pulse phase radians", phase, "Offsets this light's animation so multiple lights need not pulse together.");

            var buttons = new WrapPanel();
            AddButton(buttons, "APPLY", () =>
            {
                try
                {
                    string labelValue = (label.Text ?? "Real-time light").Trim();
                    MapLightKind kindValue = kind.SelectedItem is MapLightKind selected ? selected : MapLightKind.Point;
                    float[] positionValue = ParseVector(position.Text ?? "0,0,0", 3);
                    float[] directionValue = ParseVector(direction.Text ?? "0,-1,0", 3);
                    int[] colorValue = ParseColor(color.Text);
                    float intensityValue = Number(intensity.Text ?? "1");
                    float rangeValue = Number(range.Text ?? "18");
                    float falloffValue = Number(falloff.Text ?? "2");
                    float innerValue = Number(inner.Text ?? "24");
                    float outerValue = Number(outer.Text ?? "42");
                    float pulseAmplitudeValue = Number(pulseAmplitude.Text ?? "0");
                    float pulseSpeedValue = Number(pulseSpeed.Text ?? "0");
                    float phaseValue = Number(phase.Text ?? "0");
                    _document.Edit("Edit real-time light", map =>
                    {
                        MapLight target = map.Lights.Single(item => item.Id == id);
                        target.Label = labelValue;
                        target.Kind = kindValue;
                        target.Enabled = lightEnabled.IsChecked == true;
                        target.Position = positionValue;
                        target.Direction = directionValue;
                        target.Color = colorValue;
                        target.Intensity = intensityValue;
                        target.Range = rangeValue;
                        target.Falloff = falloffValue;
                        target.InnerConeDegrees = innerValue;
                        target.OuterConeDegrees = outerValue;
                        target.PulseAmplitude = pulseAmplitudeValue;
                        target.PulseSpeed = pulseSpeedValue;
                        target.Phase = phaseValue;
                    }, MapChangeDomain.Entity | MapChangeDomain.Environment);
                    LightingInspector();
                }
                catch (Exception ex) { Failure(ex); }
            });
            AddButton(buttons, "SELECT / FRAME", () => SelectAndFrameLight(id));
            AddButton(buttons, "PLACE AT CAMERA", () =>
            {
                if (_viewport == null) return;
                var camera = _viewport.CameraPosition;
                _document.Edit("Place light at camera", map =>
                {
                    map.Lights.Single(item => item.Id == id).Position = new[] { camera.X, camera.Y, camera.Z };
                }, MapChangeDomain.Entity);
                LightingInspector();
            });
            AddButton(buttons, "DUPLICATE", () =>
            {
                _document.Edit("Duplicate real-time light", map =>
                {
                    MapLight source = map.Lights.Single(item => item.Id == id);
                    MapLight copy = CopyLight(source);
                    copy.Id = Guid.NewGuid();
                    copy.Label = source.Label + " copy";
                    copy.Position[0] += 2;
                    map.Lights.Insert(Math.Min(authoredIndex + 1, map.Lights.Count), copy);
                }, MapChangeDomain.Entity | MapChangeDomain.Environment);
                LightingInspector();
            });
            if (index > 0) AddButton(buttons, "MOVE UP", () => MoveLight(id, -1));
            if (index + 1 < definition.Lights.Count) AddButton(buttons, "MOVE DOWN", () => MoveLight(id, 1));
            AddButton(buttons, "DELETE", () => Confirm("Delete this real-time light?", () =>
            {
                _document.Edit("Delete real-time light", map => map.Lights.RemoveAll(item => item.Id == id),
                    MapChangeDomain.Entity | MapChangeDomain.Environment);
                LightingInspector();
            }));
            content.Children.Add(buttons);
            _inspector.Children.Add(new PrimePanel(content, raised: true)
            {
                Padding = new Thickness(10), Margin = new Thickness(0, 4)
            });
        }

        AddButton(_inspector, "REMOVE ALL LOCAL LIGHTS", () => Confirm(
            "Remove every authored point and spot light? Global directional and ambient settings remain.", () =>
            {
                _document.Edit("Remove all real-time lights", map => map.Lights.Clear(),
                    MapChangeDomain.Entity | MapChangeDomain.Environment);
                LightingInspector();
            }));
    }

    private void AddLightingField(string title, Control control, string explanation)
        => AddLightingField(_inspector, title, control, explanation);

    private static void AddLightingField(Panel panel, string title, Control control, string explanation)
    {
        panel.Children.Add(PrimeChrome.Text(title, PrimeTypography.DataSmall,
            PrimeTheme.TextSecondaryBrush, data: true));
        ToolTip.SetTip(control, explanation);
        panel.Children.Add(control);
    }

    private void AddAuthoredLight(MapLightKind kind)
    {
        if (_document == null) return;
        var point = _viewport?.GetPlacementPoint() ?? System.Numerics.Vector3.Zero;
        Guid id = Guid.NewGuid();
        _document.Edit("Add real-time " + kind.ToString().ToLowerInvariant() + " light", map =>
        {
            map.Lighting ??= new();
            map.Lights ??= new();
            map.Lights.Add(new MapLight
            {
                Id = id,
                Label = kind == MapLightKind.Point ? "Point light" : "Spot light",
                Kind = kind,
                Position = new[] { point.X, point.Y + 2f, point.Z },
                Direction = new[] { 0f, -1f, 0f },
                Range = kind == MapLightKind.Point ? 18f : 28f,
                Intensity = kind == MapLightKind.Point ? 1.2f : 1.6f
            });
        }, MapChangeDomain.Entity | MapChangeDomain.Environment);
        SelectAndFrameLight(id);
        LightingInspector();
    }

    private void AddLightingRig(bool neon)
    {
        if (_document == null) return;
        var center = _viewport?.CameraTarget ?? System.Numerics.Vector3.Zero;
        _document.Edit(neon ? "Add neon arena lighting rig" : "Add three-point lighting rig", map =>
        {
            map.Lighting ??= new();
            map.Lighting.Enabled = true;
            map.Lights ??= new();
            if (neon)
            {
                map.Lighting.AmbientColor = new[] { 16, 20, 36 };
                map.Lighting.AmbientIntensity = .22f;
                map.Lights.Add(RigLight("Neon cyan", center + new System.Numerics.Vector3(-12, 7, -8), new[] { 40, 210, 255 }, 2.1f, 30));
                map.Lights.Add(RigLight("Neon magenta", center + new System.Numerics.Vector3(12, 6, 7), new[] { 255, 45, 210 }, 2.1f, 30));
                map.Lights.Add(RigLight("Neon gold", center + new System.Numerics.Vector3(0, 10, 12), new[] { 255, 170, 35 }, 1.5f, 26));
                map.Lights.Add(RigLight("Arena fill", center + new System.Numerics.Vector3(0, 14, 0), new[] { 120, 150, 255 }, 1.1f, 38));
            }
            else
            {
                map.Lighting.AmbientColor = new[] { 22, 24, 30 };
                map.Lighting.AmbientIntensity = .14f;
                map.Lights.Add(RigLight("Key light", center + new System.Numerics.Vector3(-10, 10, -8), new[] { 255, 224, 190 }, 2f, 34));
                map.Lights.Add(RigLight("Fill light", center + new System.Numerics.Vector3(10, 6, -2), new[] { 145, 180, 255 }, 1.1f, 30));
                map.Lights.Add(RigLight("Rim light", center + new System.Numerics.Vector3(0, 8, 12), new[] { 255, 120, 80 }, 1.35f, 28));
            }
        }, MapChangeDomain.Entity | MapChangeDomain.Environment);
        LightingInspector();
    }

    private static MapLight RigLight(string label, System.Numerics.Vector3 position,
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

    private void SelectAndFrameLight(Guid id)
    {
        if (_document == null) return;
        _document.Selection.Clear();
        _document.Selection.Add(id);
        _document.ActiveObjectId = id;
        _document.SelectionChanged();
        _viewport?.FrameSelection();
    }

    private void MoveLight(Guid id, int delta)
    {
        if (_document == null) return;
        _document.Edit("Reorder real-time light", map =>
        {
            int index = map.Lights.FindIndex(item => item.Id == id);
            int destination = Math.Clamp(index + delta, 0, map.Lights.Count - 1);
            if (index < 0 || destination == index) return;
            MapLight item = map.Lights[index];
            map.Lights.RemoveAt(index);
            map.Lights.Insert(destination, item);
        }, MapChangeDomain.Entity | MapChangeDomain.Environment);
        LightingInspector();
    }

    private static MapLight CopyLight(MapLight source) => new()
    {
        Id = source.Id,
        Label = source.Label,
        Enabled = source.Enabled,
        Kind = source.Kind,
        Position = (float[])source.Position.Clone(),
        Direction = (float[])source.Direction.Clone(),
        Color = (int[])source.Color.Clone(),
        Intensity = source.Intensity,
        Range = source.Range,
        InnerConeDegrees = source.InnerConeDegrees,
        OuterConeDegrees = source.OuterConeDegrees,
        Falloff = source.Falloff,
        PulseAmplitude = source.PulseAmplitude,
        PulseSpeed = source.PulseSpeed,
        Phase = source.Phase
    };

    private static int[] ParseColor(string? text)
    {
        float[] values = ParseVector(text ?? "0,0,0", 3);
        return values.Select(value => checked((int)MathF.Round(value))).ToArray();
    }

    private static string Join<T>(System.Collections.Generic.IEnumerable<T> values)
        => String.Join(",", values);

    private static string F(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
