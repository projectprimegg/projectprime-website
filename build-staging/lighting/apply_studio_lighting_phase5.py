from __future__ import annotations

import shutil
import sys
from pathlib import Path


def replace_once(path: Path, before: str, after: str, label: str) -> None:
    text = path.read_text(encoding="utf-8")
    count = text.count(before)
    if count != 1:
        raise RuntimeError(f"{label}: expected exactly one anchor, found {count} in {path}")
    path.write_text(text.replace(before, after, 1), encoding="utf-8", newline="\n")


def main() -> int:
    if len(sys.argv) != 3:
        raise SystemExit("usage: apply_studio_lighting_phase5.py <staging-directory> <project-prime-root>")

    staging = Path(sys.argv[1]).resolve()
    root = Path(sys.argv[2]).resolve()
    mapgen = root / "src/MphRead/Mods/MapGen"
    editor = root / "src/MphRead/Mods/MapEditor"
    render = root / "src/MphRead/Mods/Render"
    gui = root / "src/MphRead/Mods/Launcher/Gui"
    entities = root / "src/MphRead/Entities"

    for directory in (mapgen, editor, render, gui, entities):
        if not directory.is_dir():
            raise RuntimeError(f"Project Prime source directory is missing: {directory}")

    shutil.copy2(staging / "MapLighting.cs", mapgen / "MapLighting.cs")
    shutil.copy2(staging / "MapRuntimeLighting.cs", render / "MapRuntimeLighting.cs")
    shutil.copy2(staging / "MapStudioLighting.cs", gui / "MapStudioLighting.cs")
    shutil.copy2(staging / "MapStudioAiBeginner.cs", gui / "MapStudioAiBeginner.cs")
    shutil.copy2(staging / "AiMapEngineerLighting.cs", editor / "AiMapEngineerLighting.cs")
    shutil.copy2(staging / "MapLightingAcceptance.cs", editor / "MapLightingAcceptance.cs")

    definition = mapgen / "MapDefinition.cs"
    replace_once(
        definition,
        '''        public int[] Light2Color { get; set; } = new[] { 10, 11, 16 };
        public float[] Light2Vector { get; set; } = new[] { -0.3f, 1f, -0.2f };

        /// <summary>Optional Hardpoint rotation order by objective entity ID; unspecified nodes follow by ID.</summary>
''',
        '''        public int[] Light2Color { get; set; } = new[] { 10, 11, 16 };
        public float[] Light2Vector { get; set; } = new[] { -0.3f, 1f, -0.2f };

        /// <summary>Global controls for depth-aware authored real-time lighting.</summary>
        public MapLightingSettings Lighting { get; set; } = new();
        /// <summary>Editable real-time point and spot lights evaluated every frame.</summary>
        public List<MapLight> Lights { get; set; } = new();

        /// <summary>Optional Hardpoint rotation order by objective entity ID; unspecified nodes follow by ID.</summary>
''',
        "MapDefinition real-time lighting schema",
    )

    validator = mapgen / "Validation/MapValidator.cs"
    replace_once(
        validator,
        '''            if (!Vector(d.Light1Vector) || !Vector(d.Light2Vector)
                || d.Light1Vector.All(x => x == 0) || d.Light2Vector.All(x => x == 0))
                r.Error("FP-MAP-012", "Lighting vectors must be finite and nonzero.");
            if (d.FogSlope is < 0 or > 10 || d.FogOffset is < 0 or > 65535)
''',
        '''            if (!Vector(d.Light1Vector) || !Vector(d.Light2Vector)
                || d.Light1Vector.All(x => x == 0) || d.Light2Vector.All(x => x == 0))
                r.Error("FP-MAP-012", "Lighting vectors must be finite and nonzero.");
            MapLightingValidator.Validate(d, r);
            if (d.FogSlope is < 0 or > 10 || d.FogOffset is < 0 or > 65535)
''',
        "real-time lighting validation",
    )

    document = editor / "MapDocument.cs"
    replace_once(
        document,
        '''            MapSpawn s => s.Position, MapItem i => i.Position, MapJumpPad p => p.Position, MapNavigationLink link=>link.From, _ => new float[3]
''',
        '''            MapSpawn s => s.Position, MapItem i => i.Position, MapJumpPad p => p.Position,
            MapNavigationLink link=>link.From, MapLight light=>light.Position, _ => new float[3]
''',
        "light object position",
    )
    replace_once(
        document,
        '''            foreach (var n in d.NavigationLinks) if (n.Id == id) return new(n.Id,"Navigation",n.Kind.ToString(),n,value=>n.Id=value);
            return null;
''',
        '''            foreach (var n in d.NavigationLinks) if (n.Id == id) return new(n.Id,"Navigation",n.Kind.ToString(),n,value=>n.Id=value);
            foreach (var light in d.Lights) if (light.Id == id) return new(light.Id,"Light",light.Label,light,value=>light.Id=value);
            return null;
''',
        "find light objects",
    )
    replace_once(
        document,
        '''            foreach(var link in d.NavigationLinks)yield return new(link.Id,"Navigation",link.Kind.ToString(),link,id=>link.Id=id);
        }
''',
        '''            foreach(var link in d.NavigationLinks)yield return new(link.Id,"Navigation",link.Kind.ToString(),link,id=>link.Id=id);
            foreach(var light in d.Lights)yield return new(light.Id,"Light",light.Label,light,id=>light.Id=id);
        }
''',
        "enumerate light objects",
    )
    replace_once(
        document,
        '''            d.NavigationLinks.RemoveAll(g=>ids.Contains(g.Id));
''',
        '''            d.NavigationLinks.RemoveAll(g=>ids.Contains(g.Id)); d.Lights.RemoveAll(g=>ids.Contains(g.Id));
''',
        "delete light objects",
    )
    replace_once(
        document,
        '''                    case MapNavigationLink link: copy.NavigationLinks.Add(link); break;
''',
        '''                    case MapNavigationLink link: copy.NavigationLinks.Add(link); break;
                    case MapLight light: copy.Lights.Add(light); break;
''',
        "copy light objects",
    )
    replace_once(
        document,
        '''                    case MapNavigationLink link:d.NavigationLinks.Add(link);break;
''',
        '''                    case MapNavigationLink link:d.NavigationLinks.Add(link);break;
                    case MapLight light:d.Lights.Add(light);break;
''',
        "duplicate light objects",
    )

    render_options = root / "src/MphRead/Mods/RenderOptions.cs"
    replace_once(
        render_options,
        '''        public static bool NeedsReadableDepth => Shadows != ShadowQuality.Off;
        public static bool PostProcessingEnabled => Shadows != ShadowQuality.Off;
''',
        '''        internal static bool CustomMapLightingActive { get; set; }
        public static bool NeedsReadableDepth => Shadows != ShadowQuality.Off || CustomMapLightingActive;
        public static bool PostProcessingEnabled => Shadows != ShadowQuality.Off || CustomMapLightingActive;
''',
        "real-time lighting render target policy",
    )

    room = entities / "RoomEntity.cs"
    replace_once(
        room,
        '''            Mods.MapGen.MapRuntimeUsage.Track(_scene, meta.Name);
            // todo: unlock the corresponding multiplayer arena when visiting a new planet
''',
        '''            Mods.MapGen.MapRuntimeUsage.Track(_scene, meta.Name);
            _scene.BindAuthoredMapLighting(meta.Name);
            // todo: unlock the corresponding multiplayer arena when visiting a new planet
''',
        "bind room lighting recipe",
    )

    screen = gui / "MapStudioScreen.cs"
    replace_once(
        screen,
        '''            AddButton(toolbar, "ASSETS", () => ShowInspectorPage("Assets & music"));
            AddButton(toolbar, "POP OUT", () => _ = PopOut());
''',
        '''            AddButton(toolbar, "ASSETS", () => ShowInspectorPage("Assets & music"));
            AddButton(toolbar, "LIGHTING", () => ShowInspectorPage("Lighting"));
            AddButton(toolbar, "POP OUT", () => _ = PopOut());
''',
        "advanced lighting toolbar",
    )
    replace_once(
        screen,
        '''            Choice(new[]{"Add object","Box","Wedge","Prism","Convex","Mesh","Spawn","Pickup","Jump pad","Navigation link"},name=>{if(name!="Add object")AddObject(name);});
''',
        '''            Choice(new[]{"Add object","Box","Wedge","Prism","Convex","Mesh","Spawn","Pickup","Jump pad","Navigation link","Point light","Spot light"},name=>{if(name!="Add object")AddObject(name);});
''',
        "lighting add-object choices",
    )
    replace_once(
        screen,
        '''            Choice(new[]{"Inspector","Modeling","Partitioning","Collision repairs","Environment","Materials","Assets & music","Snapping","Arrange","Layers","Map health","Navigation path","Statistics"},name=>ShowInspectorPage(name));
''',
        '''            Choice(new[]{"Inspector","Modeling","Partitioning","Collision repairs","Environment","Lighting","Materials","Assets & music","Snapping","Arrange","Layers","Map health","Navigation path","Statistics"},name=>ShowInspectorPage(name));
''',
        "lighting inspector choice",
    )
    replace_once(
        screen,
        '''            var healthQuick = new PrimeButton("HEALTH",
                () => ShowInspectorPage("Map health"), compact: true);
''',
        '''            var healthQuick = new PrimeButton("HEALTH",
                () => ShowInspectorPage("Map health"), compact: true);
            var lightingQuick = new PrimeButton("LIGHTING",
                () => ShowInspectorPage("Lighting"), compact: true);
''',
        "lighting quick button",
    )
    replace_once(
        screen,
        '''            ControllerNav.Identify(healthQuick, "studio.health");
            var inspectorQuick = PrimeChrome.Columns("*,*,*,*",
                inspectQuick, materialsQuick, assetsQuick, healthQuick);
''',
        '''            ControllerNav.Identify(healthQuick, "studio.health");
            ControllerNav.Identify(lightingQuick, "studio.lighting");
            var inspectorQuick = PrimeChrome.Columns("*,*,*,*,*",
                inspectQuick, materialsQuick, assetsQuick, lightingQuick, healthQuick);
''',
        "lighting quick navigation",
    )
    replace_once(
        screen,
        '''                string[] filters=new[]{"All","Geometry","Spawns","Pickups","Jump Pads","Navigation"}
''',
        '''                string[] filters=new[]{"All","Geometry","Spawns","Pickups","Jump Pads","Navigation","Lights"}
''',
        "hierarchy lighting filter",
    )
    replace_once(
        screen,
        '''                    MapNavigationLink=>"Navigation",
                    _=>"Other"
''',
        '''                    MapNavigationLink=>"Navigation",
                    MapLight=>"Lighting",
                    _=>"Other"
''',
        "hierarchy lighting group",
    )
    replace_once(
        screen,
        '''                    if(filter=="Navigation")return o.Value is MapNavigationLink;
''',
        '''                    if(filter=="Navigation")return o.Value is MapNavigationLink;
                    if(filter=="Lights")return o.Value is MapLight;
''',
        "hierarchy lighting match",
    )
    replace_once(
        screen,
        '''                new("Add Navigation Link","create navigation link",()=>AddObject("Navigation link")),
''',
        '''                new("Add Navigation Link","create navigation link",()=>AddObject("Navigation link")),
                new("Add Point Light","lighting real-time point",()=>AddObject("Point light")),
                new("Add Spot Light","lighting real-time cone",()=>AddObject("Spot light")),
                new("Show Lighting Editor","lighting point spot ambient exposure",()=>ShowInspectorPage("Lighting")),
''',
        "lighting command palette",
    )
    replace_once(
        screen,
        '''                    case "Navigation link":
                    {
                        var value=new MapNavigationLink{From=new[]{place.X,place.Y+.1f,place.Z},To=new[]{place.X+6,place.Y+.1f,place.Z}};
                        created=value.Id;d.NavigationLinks.Add(value);break;
                    }
''',
        '''                    case "Navigation link":
                    {
                        var value=new MapNavigationLink{From=new[]{place.X,place.Y+.1f,place.Z},To=new[]{place.X+6,place.Y+.1f,place.Z}};
                        created=value.Id;d.NavigationLinks.Add(value);break;
                    }
                    case "Point light":
                    case "Spot light":
                    {
                        var value=new MapLight
                        {
                            Label=kind,
                            Kind=kind=="Spot light"?MapLightKind.Spot:MapLightKind.Point,
                            Position=new[]{place.X,place.Y+2,place.Z},
                            Direction=new[]{0f,-1f,0f},
                            Range=kind=="Spot light"?28f:18f,
                            Intensity=kind=="Spot light"?1.6f:1.2f
                        };
                        created=value.Id;d.Lights.Add(value);break;
                    }
''',
        "create authored lights",
    )
    replace_once(
        screen,
        '''                case "Environment": EnvironmentInspector(); break;
                case "Materials": MaterialInspector(); break;
''',
        '''                case "Environment": EnvironmentInspector(); break;
                case "Lighting": LightingInspector(); break;
                case "Materials": MaterialInspector(); break;
''',
        "lighting inspector dispatch",
    )

    viewport = gui / "MapViewport.cs"
    replace_once(
        viewport,
        '''                IBrush color = warning ? Brushes.OrangeRed : o.Value is MapSpawn teamSpawn
                    ? teamSpawn.Team == 0 ? Brushes.DeepSkyBlue : teamSpawn.Team == 1 ? Brushes.Orange : Brushes.LimeGreen
                    : o.Value is MapItem ? Brushes.DeepSkyBlue : Brushes.Magenta;
''',
        '''                IBrush color = warning ? Brushes.OrangeRed : o.Value is MapSpawn teamSpawn
                    ? teamSpawn.Team == 0 ? Brushes.DeepSkyBlue : teamSpawn.Team == 1 ? Brushes.Orange : Brushes.LimeGreen
                    : o.Value is MapItem ? Brushes.DeepSkyBlue
                    : o.Value is MapLight authoredLight
                        ? new SolidColorBrush(Color.FromRgb((byte)Math.Clamp(authoredLight.Color.ElementAtOrDefault(0),0,255),
                            (byte)Math.Clamp(authoredLight.Color.ElementAtOrDefault(1),0,255),
                            (byte)Math.Clamp(authoredLight.Color.ElementAtOrDefault(2),0,255)))
                        : Brushes.Magenta;
''',
        "viewport light color",
    )
    replace_once(
        viewport,
        '''                if(o.Value is MapNavigationLink link)Line(context,position,MapViewportScene.Vector(link.To),Brushes.Orange,3);
                if(o.Value is MapJumpPad triggerPad&&EntityVisualization&&triggerPad.Size?.Length==3)
''',
        '''                if(o.Value is MapNavigationLink link)Line(context,position,MapViewportScene.Vector(link.To),Brushes.Orange,3);
                if(o.Value is MapLight light && (EntityVisualization || Document.Selection.Contains(o.Id)))
                {
                    float radius=Math.Clamp(light.Range,.25f,128f);
                    Ring(context,position,radius,color,Document.Selection.Contains(o.Id)?2:1);
                    Vector direction=light.Direction?.Length==3
                        ?new Vector(light.Direction[0],light.Direction[1],light.Direction[2]):new Vector(0,-1,0);
                    if(direction.LengthSquared()<.0001f)direction=new Vector(0,-1,0);else direction=Vector.Normalize(direction);
                    Line(context,position,position+direction*Math.Min(radius,8),color,2);
                    if(light.Kind==MapLightKind.Spot)
                    {
                        float angle=Math.Clamp(light.OuterConeDegrees,1,89)*MathF.PI/180;
                        Vector helper=Math.Abs(Vector.Dot(direction,Vector.UnitY))>.9f?Vector.UnitX:Vector.UnitY;
                        Vector right=Vector.Normalize(Vector.Cross(direction,helper));
                        Vector up=Vector.Normalize(Vector.Cross(right,direction));
                        float length=Math.Min(radius,10), spread=MathF.Tan(angle)*length;
                        Vector end=position+direction*length;
                        Line(context,position,end+right*spread,color);
                        Line(context,position,end-right*spread,color);
                        Line(context,position,end+up*spread,color);
                        Line(context,position,end-up*spread,color);
                    }
                    Label(context,o.Id,$"{light.Label} · {light.Kind} · {light.Intensity:0.##} × {light.Range:0.##}",position+Vector.UnitY*.8f);
                }
                if(o.Value is MapJumpPad triggerPad&&EntityVisualization&&triggerPad.Size?.Length==3)
''',
        "viewport lighting gizmos",
    )

    experience = gui / "MapStudioExperience.cs"
    replace_once(
        experience,
        '''        var tests = new PrimeButton("AUTO TEST", () => _ = RunStudioAcceptanceFromUi(), compact: true)
        {
            Margin = new Thickness(2), MinHeight = 28, Height = 28
        };
        ToolTip.SetTip(beginner, StudioHelpCatalog.Get("mode.beginner").Summary);
''',
        '''        var tests = new PrimeButton("AUTO TEST", () => _ = RunStudioAcceptanceFromUi(), compact: true)
        {
            Margin = new Thickness(2), MinHeight = 28, Height = 28
        };
        var aiAssist = new PrimeButton("AI ASSIST", ShowBeginnerAiAssistant, primary: true, compact: true)
        {
            Margin = new Thickness(2), MinHeight = 28, Height = 28
        };
        ToolTip.SetTip(beginner, StudioHelpCatalog.Get("mode.beginner").Summary);
''',
        "Beginner AI Assistant control",
    )
    replace_once(
        experience,
        '''        ToolTip.SetTip(tests, "Runs the self-contained Studio acceptance suite without requiring manual map editing.");
''',
        '''        ToolTip.SetTip(tests, "Runs the self-contained Studio acceptance suite without requiring manual map editing.");
        ToolTip.SetTip(aiAssist, "Ask AI Map Engineer what to do next, check the map, or create safe starter lighting.");
''',
        "Beginner AI Assistant tooltip",
    )
    replace_once(
        experience,
        '''        ControllerNav.Identify(tests, "studio.acceptance");
''',
        '''        ControllerNav.Identify(tests, "studio.acceptance");
        ControllerNav.Identify(aiAssist, "studio.ai.beginner");
''',
        "Beginner AI Assistant navigation",
    )
    replace_once(
        experience,
        '''        toolbar.Children.Add(tests);
        _editingControls.Add(beginner);
''',
        '''        toolbar.Children.Add(tests);
        toolbar.Children.Add(aiAssist);
        _editingControls.Add(beginner);
''',
        "Beginner AI Assistant toolbar",
    )
    replace_once(
        experience,
        '''        _editingControls.Add(tests);

        ApplyStudioExperienceMode(save: false);
''',
        '''        _editingControls.Add(tests);
        _editingControls.Add(aiAssist);

        ApplyStudioExperienceMode(save: false);
''',
        "Beginner AI Assistant busy state",
    )
    replace_once(
        experience,
        '''            AddButton(utilities, "AUTOMATED TESTS", () => _ = RunStudioAcceptanceFromUi());
''',
        '''            AddButton(utilities, "AI MAP ENGINEER", ShowBeginnerAiAssistant);
            AddButton(utilities, "REAL-TIME LIGHTING", () => ShowInspectorPage("Lighting"));
            AddButton(utilities, "AUTOMATED TESTS", () => _ = RunStudioAcceptanceFromUi());
''',
        "Beginner guide AI and lighting actions",
    )

    ai = editor / "AiMapEngineer.cs"
    replace_once(ai, "public sealed class AiMapEngineerSession\n", "public sealed partial class AiMapEngineerSession\n",
        "AI Engineer partial lighting extension")
    replace_once(
        ai,
        '''        new AiMapEngineerToolDefinition("inspect_studio_guidance", false, "Evaluate Beginner Mode and return the evidence-based next step. arguments: workflow, built, completed."),
        new AiMapEngineerToolDefinition("find_objects", false, "Find objects by optional kind and label text. arguments: kind, label."),
''',
        '''        new AiMapEngineerToolDefinition("inspect_studio_guidance", false, "Evaluate Beginner Mode and return the evidence-based next step. arguments: workflow, built, completed."),
        new AiMapEngineerToolDefinition("inspect_lighting", false, "Inspect global lighting and every authored point/spot light."),
        new AiMapEngineerToolDefinition("add_light", true, "Add a fully configurable real-time light. arguments: kind, label, position, direction, color, intensity, range, falloff, cone and pulse values."),
        new AiMapEngineerToolDefinition("update_light", true, "Update an authored real-time light by id using supplied fields."),
        new AiMapEngineerToolDefinition("set_lighting_globals", true, "Set ambient color/intensity, exposure, diffuse/specular strength and active-light limit."),
        new AiMapEngineerToolDefinition("create_lighting_rig", true, "Create a safe three-point or neon-arena lighting rig. arguments: preset, center."),
        new AiMapEngineerToolDefinition("find_objects", false, "Find objects by optional kind and label text. arguments: kind, label."),
''',
        "AI lighting tool contracts",
    )
    replace_once(
        ai,
        '''                "inspect_studio_guidance" => InspectStudioGuidance(document, request.Arguments),
                "find_objects" => FindObjects(document, request.Arguments),
''',
        '''                "inspect_studio_guidance" => InspectStudioGuidance(document, request.Arguments),
                "inspect_lighting" => InspectLighting(document),
                "add_light" => AddLight(document, request.Arguments),
                "update_light" => UpdateLight(document, request.Arguments),
                "set_lighting_globals" => SetLightingGlobals(document, request.Arguments),
                "create_lighting_rig" => CreateLightingRig(document, request.Arguments),
                "find_objects" => FindObjects(document, request.Arguments),
''',
        "AI lighting tool dispatch",
    )
    replace_once(
        ai,
        '''            assets = d.Assets.Count,
            hasRenderImport = d.Import != null,
''',
        '''            assets = d.Assets.Count,
            authoredLights = d.Lights.Count,
            realTimeLightingEnabled = d.Lighting.Enabled,
            hasRenderImport = d.Import != null,
''',
        "AI map lighting inspection summary",
    )

    acceptance = editor / "StudioExperienceAcceptance.cs"
    replace_once(
        acceptance,
        '''                ModelReimport.Apply(definition, imported, modelPath,
                    MapSourceFingerprint.Hash(imported.Dependencies), settings);
                bool applied = definition.ModelSources.Count == 1
''',
        '''                ModelReimport.Apply(definition, imported, modelPath,
                    MapSourceFingerprint.Hash(imported.Dependencies), settings);
                definition.Lighting = new MapLightingSettings
                {
                    Enabled = true,
                    AmbientColor = new[] { 18, 22, 30 },
                    AmbientIntensity = .18f,
                    Exposure = 1.05f,
                    DiffuseStrength = 1.2f,
                    SpecularStrength = .25f,
                    RuntimeLightLimit = 16
                };
                definition.Lights.Add(new MapLight
                {
                    Label = "Acceptance point",
                    Kind = MapLightKind.Point,
                    Position = new[] { 0f, 4f, 0f },
                    Color = new[] { 255, 220, 180 },
                    Intensity = 1.5f,
                    Range = 24f
                });
                definition.Lights.Add(new MapLight
                {
                    Label = "Acceptance spot",
                    Kind = MapLightKind.Spot,
                    Position = new[] { 2f, 6f, 2f },
                    Direction = new[] { -.2f, -1f, -.2f },
                    Color = new[] { 120, 170, 255 },
                    Intensity = 1.8f,
                    Range = 30f,
                    InnerConeDegrees = 20f,
                    OuterConeDegrees = 38f,
                    PulseAmplitude = .25f,
                    PulseSpeed = 1f
                });
                bool applied = definition.ModelSources.Count == 1
''',
        "acceptance real-time lighting fixture",
    )
    replace_once(
        acceptance,
        '''            Check(checks, "Beginner workflow recommends a real next step", () =>
''',
        '''            if (definition != null) MapLightingAcceptance.Append(checks, definition);

            Check(checks, "Beginner workflow recommends a real next step", () =>
''',
        "lighting acceptance suite",
    )

    graphics = render / "GraphicsPipeline.cs"
    replace_once(
        graphics,
        '''using MphRead.Mods;
using OpenTK.Graphics.OpenGL;
''',
        '''using MphRead.Mods;
using MphRead.Mods.MapGen;
using OpenTK.Graphics.OpenGL;
''',
        "graphics lighting namespace",
    )
    replace_once(
        graphics,
        '''        private int _gfxShadowTexel;
        private int _gfxShadowLightDir;
''',
        '''        private int _gfxShadowTexel;
        private int _gfxShadowLightDir;
        private int _gfxUseDirectionalShadow;
        private int _gfxDynamicLightCount;
        private int _gfxDynamicAmbient;
        private int _gfxDynamicSettings;
        private readonly int[] _gfxDynamicLightPositionRange = new int[MapLightingRuntime.MaxRuntimeLights];
        private readonly int[] _gfxDynamicLightColorIntensity = new int[MapLightingRuntime.MaxRuntimeLights];
        private readonly int[] _gfxDynamicLightDirectionOuter = new int[MapLightingRuntime.MaxRuntimeLights];
        private readonly int[] _gfxDynamicLightParameters = new int[MapLightingRuntime.MaxRuntimeLights];
''',
        "graphics lighting uniforms",
    )
    replace_once(
        graphics,
        '''            _graphicsOutputReady = false;
            if (_graphicsPipelineRefused
                || !ShouldCompositeDirectionalShadow(
                    RenderOptions.Shadows, ShadowMapReady, _depthTexture != 0))
            {
                // The scene's original backbuffer is composited directly.
                // Delete the optional shadow color target when shadows are
                // switched off, without holding a large GPU allocation until
                // this scene is unloaded.
                if (_graphicsOutputTexture != 0
                    && (_graphicsPipelineRefused
                        || RenderOptions.Shadows == ShadowQuality.Off))
                    ReleaseGraphicsOutputTarget();
                return;
            }
''',
        '''            _graphicsOutputReady = false;
            bool directionalShadow = ShouldCompositeDirectionalShadow(
                RenderOptions.Shadows, ShadowMapReady, _depthTexture != 0);
            bool authoredLighting = HasAuthoredRuntimeLighting && _depthTexture != 0;
            if (_graphicsPipelineRefused || (!directionalShadow && !authoredLighting))
            {
                if (_graphicsOutputTexture != 0)
                    ReleaseGraphicsOutputTarget();
                return;
            }
''',
        "graphics lighting compositor gate",
    )
    replace_once(
        graphics,
        '''                GL.BindTexture(TextureTarget.Texture2D, _shadowDepthTexture);
                GL.Uniform1(_gfxShadowSampler, 2);

                GL.Uniform2(_gfxTexel,
''',
        '''                GL.BindTexture(TextureTarget.Texture2D, directionalShadow ? _shadowDepthTexture : 0);
                GL.Uniform1(_gfxShadowSampler, 2);
                GL.Uniform1(_gfxUseDirectionalShadow, directionalShadow ? 1 : 0);

                GL.Uniform2(_gfxTexel,
''',
        "optional directional shadow binding",
    )
    replace_once(
        graphics,
        '''                GL.Uniform3(_gfxShadowLightDir, direction.Normalized());

                DrawGraphicsFullscreenQuad();
''',
        '''                GL.Uniform3(_gfxShadowLightDir, direction.Normalized());

                MapLightingUniformPacket lighting = CurrentAuthoredLighting();
                GL.Uniform1(_gfxDynamicLightCount, lighting.Count);
                GL.Uniform4(_gfxDynamicAmbient, lighting.Ambient[0], lighting.Ambient[1],
                    lighting.Ambient[2], lighting.Ambient[3]);
                GL.Uniform4(_gfxDynamicSettings, lighting.Settings[0], lighting.Settings[1],
                    lighting.Settings[2], lighting.Settings[3]);
                for (int index = 0; index < MapLightingRuntime.MaxRuntimeLights; index++)
                {
                    float[] p = lighting.PositionRange[index];
                    float[] c = lighting.ColorIntensity[index];
                    float[] d = lighting.DirectionOuter[index];
                    float[] parameters = lighting.Parameters[index];
                    GL.Uniform4(_gfxDynamicLightPositionRange[index], p[0], p[1], p[2], p[3]);
                    GL.Uniform4(_gfxDynamicLightColorIntensity[index], c[0], c[1], c[2], c[3]);
                    GL.Uniform4(_gfxDynamicLightDirectionOuter[index], d[0], d[1], d[2], d[3]);
                    GL.Uniform4(_gfxDynamicLightParameters[index], parameters[0], parameters[1], parameters[2], parameters[3]);
                }

                DrawGraphicsFullscreenQuad();
''',
        "upload authored real-time lights",
    )
    replace_once(
        graphics,
        '''                    _gfxShadowTexel = GL.GetUniformLocation(_graphicsProgram, "shadow_texel");
                    _gfxShadowLightDir = GL.GetUniformLocation(_graphicsProgram, "shadow_light_dir");
''',
        '''                    _gfxShadowTexel = GL.GetUniformLocation(_graphicsProgram, "shadow_texel");
                    _gfxShadowLightDir = GL.GetUniformLocation(_graphicsProgram, "shadow_light_dir");
                    _gfxUseDirectionalShadow = GL.GetUniformLocation(_graphicsProgram, "use_directional_shadow");
                    _gfxDynamicLightCount = GL.GetUniformLocation(_graphicsProgram, "dynamic_light_count");
                    _gfxDynamicAmbient = GL.GetUniformLocation(_graphicsProgram, "dynamic_ambient");
                    _gfxDynamicSettings = GL.GetUniformLocation(_graphicsProgram, "dynamic_settings");
                    for (int index = 0; index < MapLightingRuntime.MaxRuntimeLights; index++)
                    {
                        _gfxDynamicLightPositionRange[index] = GL.GetUniformLocation(_graphicsProgram, $"dynamic_light_pos_range[{index}]");
                        _gfxDynamicLightColorIntensity[index] = GL.GetUniformLocation(_graphicsProgram, $"dynamic_light_color_intensity[{index}]");
                        _gfxDynamicLightDirectionOuter[index] = GL.GetUniformLocation(_graphicsProgram, $"dynamic_light_direction_outer[{index}]");
                        _gfxDynamicLightParameters[index] = GL.GetUniformLocation(_graphicsProgram, $"dynamic_light_parameters[{index}]");
                    }
''',
        "resolve authored lighting uniforms",
    )
    replace_once(
        graphics,
        '''            _graphicsPipelineRefused = false;
            DeleteProgram(ref _graphicsProgram);
''',
        '''            _graphicsPipelineRefused = false;
            RenderOptions.CustomMapLightingActive = false;
            DeleteProgram(ref _graphicsProgram);
''',
        "release custom lighting render policy",
    )
    replace_once(
        graphics,
        '''uniform vec2 shadow_texel;
uniform vec3 shadow_light_dir;
''',
        '''uniform vec2 shadow_texel;
uniform vec3 shadow_light_dir;
uniform int use_directional_shadow;
uniform int dynamic_light_count;
uniform vec4 dynamic_ambient;
uniform vec4 dynamic_settings;
uniform vec4 dynamic_light_pos_range[16];
uniform vec4 dynamic_light_color_intensity[16];
uniform vec4 dynamic_light_direction_outer[16];
uniform vec4 dynamic_light_parameters[16];
''',
        "lighting shader uniforms",
    )
    replace_once(
        graphics,
        '''float directional_shadow(vec3 worldPos, vec3 viewNormal) {
    vec4 lightClip = shadow_projection * shadow_view * vec4(worldPos, 1.0);
''',
        '''float directional_shadow(vec3 worldPos, vec3 viewNormal) {
    if (use_directional_shadow == 0) return 1.0;
    vec4 lightClip = shadow_projection * shadow_view * vec4(worldPos, 1.0);
''',
        "optional directional shadow shader",
    )
    replace_once(
        graphics,
        '''    return mix(0.58, 1.0, lit);
}

void main() {
    vec2 uv = texcoord;
    vec3 color = SAMPLE(tex, clamp(uv, vec2(0.0), vec2(1.0))).rgb;
    float d = raw_depth(uv);
    if (d < 0.999999) {
        vec3 worldPos = world_position(uv, d);
        vec3 normal = depth_normal(uv, d);
        color *= directional_shadow(worldPos, normal);
    }
    OUTPUT = vec4(clamp(color, 0.0, 1.0), 1.0);
}
''',
        '''    return mix(0.58, 1.0, lit);
}

vec3 apply_dynamic_lighting(vec3 baseColor, vec3 worldPos, vec3 worldNormal) {
    vec3 contribution = baseColor * dynamic_ambient.rgb * dynamic_ambient.a;
    vec3 cameraWorld = (inv_view * vec4(0.0, 0.0, 0.0, 1.0)).xyz;
    vec3 viewDirection = normalize(cameraWorld - worldPos);
    for (int i = 0; i < 16; i++) {
        if (i < dynamic_light_count) {
            vec3 toLight = dynamic_light_pos_range[i].xyz - worldPos;
            float distanceToLight = length(toLight);
            float range = max(dynamic_light_pos_range[i].w, 0.0001);
            if (distanceToLight < range && distanceToLight > 0.0001) {
                vec3 lightDirection = toLight / distanceToLight;
                float radial = clamp(1.0 - distanceToLight / range, 0.0, 1.0);
                float attenuation = pow(radial, max(dynamic_light_parameters[i].z, 0.25));
                float spot = 1.0;
                if (dynamic_light_parameters[i].x > 0.5) {
                    float cone = dot(normalize(dynamic_light_direction_outer[i].xyz), -lightDirection);
                    spot = smoothstep(dynamic_light_direction_outer[i].w,
                        dynamic_light_parameters[i].y, cone);
                }
                float diffuseTerm = max(dot(worldNormal, lightDirection), 0.0);
                vec3 radiance = dynamic_light_color_intensity[i].rgb
                    * dynamic_light_color_intensity[i].a * attenuation * spot;
                contribution += baseColor * radiance * diffuseTerm * dynamic_settings.y;
                vec3 halfwayDirection = normalize(lightDirection + viewDirection);
                float specularTerm = pow(max(dot(worldNormal, halfwayDirection), 0.0), 32.0);
                contribution += radiance * specularTerm * dynamic_settings.z;
            }
        }
    }
    return (baseColor + contribution) * dynamic_settings.x;
}

void main() {
    vec2 uv = texcoord;
    vec3 color = SAMPLE(tex, clamp(uv, vec2(0.0), vec2(1.0))).rgb;
    float d = raw_depth(uv);
    if (d < 0.999999) {
        vec3 worldPos = world_position(uv, d);
        vec3 viewNormal = depth_normal(uv, d);
        vec3 worldNormal = normalize(mat3(inv_view) * viewNormal);
        color = apply_dynamic_lighting(color, worldPos, worldNormal);
        color *= directional_shadow(worldPos, viewNormal);
    }
    OUTPUT = vec4(clamp(color, 0.0, 1.0), 1.0);
}
''',
        "depth-aware point and spot lighting shader",
    )

    print("Studio Lighting Phase 5 integrated into", root)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
