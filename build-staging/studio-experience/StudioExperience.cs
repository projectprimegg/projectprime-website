using System;
using System.Collections.Generic;
using System.Linq;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.MapEditor;

public enum StudioExperienceMode
{
    Beginner,
    Advanced
}

public enum StudioWorkflow
{
    ImportExternalModel,
    BuildFromScratch,
    EditExistingMap,
    PrepareForOnline
}

public sealed record StudioGuidanceStep(
    string Id,
    string Title,
    string Summary,
    string ActionId,
    bool Complete,
    bool Blocking,
    string Status,
    bool CanMarkComplete = false);

public sealed record StudioGuidanceSnapshot(
    StudioWorkflow Workflow,
    IReadOnlyList<StudioGuidanceStep> Steps,
    string? NextStepId,
    int CompleteCount,
    int TotalCount,
    bool ReadyToPackage,
    string Summary)
{
    public StudioGuidanceStep? NextStep => NextStepId == null
        ? null
        : Steps.FirstOrDefault(step => step.Id == NextStepId);
}

public sealed record StudioHelpTopic(
    string Id,
    string Title,
    string Summary,
    IReadOnlyList<string> Steps,
    IReadOnlyList<string> CommonMistakes,
    string SearchTerms);

/// <summary>
/// Pure, deterministic workflow guidance shared by the Map Studio UI, automated
/// acceptance tests and the AI Map Engineer. It does not mutate a map and does
/// not depend on Avalonia, so recommendations can be checked headlessly in CI.
/// </summary>
public static class StudioExperienceEngine
{
    public static StudioGuidanceSnapshot Evaluate(
        MapDefinition? map,
        string? projectPath,
        MapValidationResult? validation,
        bool hasSuccessfulBuild,
        StudioWorkflow workflow,
        ISet<string>? acknowledgedSteps = null)
    {
        acknowledgedSteps ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool loaded = map != null;
        bool saved = loaded && !String.IsNullOrWhiteSpace(projectPath);
        bool imported = loaded && (map!.ModelSources.Count > 0 || map.Import != null || map.NativeRoom != null);
        bool geometry = loaded && (map!.Geometry.Count > 0 || map.Brushes.Count > 0 || imported);
        bool collision = loaded && (map!.Collision != null
            || map.Geometry.Any(item => item.Solid || item is MapMesh { CollisionOnly: true })
            || map.Brushes.Count > 0);
        bool spawns = loaded && map!.Spawns.Count > 0;
        bool gameplay = loaded && (map!.Items.Count > 0 || map.JumpPads.Count > 0
            || acknowledgedSteps.Contains("gameplay-reviewed"));
        bool materials = loaded && map!.Materials.Count > 0;
        bool modes = loaded && map!.Capabilities?.SupportedModes?.Count > 0;
        bool validated = validation != null;
        bool blockingErrors = validation?.Diagnostics.Any(d => d.Severity == MapDiagnosticSeverity.Error) == true;
        bool valid = validated && !blockingErrors;
        bool audited = acknowledgedSteps.Contains("runtime-audit");
        bool playtested = acknowledgedSteps.Contains("playtest");
        bool orientationReviewed = acknowledgedSteps.Contains("orientation-reviewed");
        bool changesReviewed = acknowledgedSteps.Contains("changes-reviewed");

        var steps = workflow switch
        {
            StudioWorkflow.BuildFromScratch => new List<StudioGuidanceStep>
            {
                Step("project", "Create or open a project", "Start with a template that matches the kind of map you want to make.", "create_project", loaded, true, loaded ? "Project loaded" : "No project is open"),
                Step("save", "Save the editable project", "Saving first gives imported textures, generated files and recovery data a safe home.", "save_project", saved, true, saved ? "Editable project has a file" : "Project has not been saved"),
                Step("geometry", "Block out the playable space", "Create floors, walls, ramps and routes before spending time on visual detail.", "create_geometry", geometry, true, geometry ? "Geometry exists" : "No playable geometry exists"),
                Step("collision", "Check movement collision", "Every intended floor and wall must have movement-safe collision.", "collision_review", collision, true, collision ? "Collision is present" : "Collision is missing"),
                Step("spawns", "Add safe player spawns", "Players need valid floor, headroom and team assignments before a match can start.", "add_spawn", spawns, true, spawns ? $"{map!.Spawns.Count} spawn(s)" : "No player spawns"),
                Step("gameplay-reviewed", "Add or review gameplay objects", "Place weapons, health, jump pads and objectives only after the routes work.", "add_pickup", gameplay, false, gameplay ? "Gameplay objects reviewed" : "Optional gameplay pass not completed", true),
                Step("materials", "Set materials and environment", "Make surfaces readable and verify texture scale before final lighting.", "materials", materials, false, materials ? $"{map!.Materials.Count} material(s)" : "No materials"),
                Step("validate", "Validate the map", "Validation catches invalid geometry, missing sources, spawn problems and budget failures.", "validate", valid, true, valid ? "Validation has no blocking errors" : validated ? "Validation found blocking errors" : "Validation has not run"),
                Step("playtest", "Playtest the routes", "Walk, jump and use alt forms through every intended route.", "playtest", playtested, true, playtested ? "Playtest acknowledged" : "Playtest not acknowledged", true),
                Step("build", "Build the .ppmap", "Create the distributable package only after validation and playtesting pass.", "build_package", hasSuccessfulBuild, true, hasSuccessfulBuild ? "Package build succeeded" : "No successful package build")
            },
            StudioWorkflow.EditExistingMap => new List<StudioGuidanceStep>
            {
                Step("project", "Open the map project", "Open an editable JSON project or make an editable copy of a package.", "open_project", loaded, true, loaded ? "Project loaded" : "No project is open"),
                Step("save", "Save a working copy", "Keep the original untouched until the revised map passes its tests.", "save_project", saved, true, saved ? "Working copy saved" : "Working copy is not saved"),
                Step("changes-reviewed", "Make and inspect the intended change", "Keep the change focused, then compare it against the original behavior.", "show_inspector", changesReviewed, true, changesReviewed ? "Change reviewed" : "Change still needs review", true),
                Step("collision", "Recheck affected collision", "Geometry edits can introduce seams, blockers or mismatched collision.", "collision_review", collision, true, collision ? "Collision is present" : "Collision is missing"),
                Step("validate", "Validate the revised map", "Do not package a revision with new blocking diagnostics.", "validate", valid, true, valid ? "Validation passed" : validated ? "Validation found blocking errors" : "Validation has not run"),
                Step("playtest", "Playtest the changed area", "Verify the exact route, object or material behavior that was modified.", "playtest", playtested, true, playtested ? "Playtest acknowledged" : "Playtest not acknowledged", true),
                Step("runtime-audit", "Run the runtime audit", "The audit checks that the map can be loaded and exercised by the runtime harness.", "audit", audited, true, audited ? "Runtime audit acknowledged" : "Runtime audit not acknowledged", true),
                Step("build", "Build the revision", "Package the verified revision without overwriting your source project.", "build_package", hasSuccessfulBuild, true, hasSuccessfulBuild ? "Package build succeeded" : "No successful package build")
            },
            StudioWorkflow.PrepareForOnline => new List<StudioGuidanceStep>
            {
                Step("project", "Open the finished project", "Online preparation starts from an editable project, not an unknown loose package.", "open_project", loaded, true, loaded ? "Project loaded" : "No project is open"),
                Step("save", "Save the current revision", "All participants must receive the same deterministic revision.", "save_project", saved, true, saved ? "Revision saved" : "Revision is not saved"),
                Step("geometry", "Confirm playable geometry", "The map must contain a complete playable space.", "frame_all", geometry, true, geometry ? "Geometry exists" : "No playable geometry"),
                Step("collision", "Confirm collision", "Online movement cannot depend on a local-only editor approximation.", "collision_review", collision, true, collision ? "Collision is present" : "Collision is missing"),
                Step("spawns", "Confirm multiplayer spawns", "Every supported team and mode needs safe, reachable spawn positions.", "add_spawn", spawns, true, spawns ? $"{map!.Spawns.Count} spawn(s)" : "No player spawns"),
                Step("modes", "Confirm supported modes", "Declare only modes whose required objectives and entities are present.", "map_health", modes, true, modes ? "Supported modes are declared" : "No supported modes are declared"),
                Step("validate", "Run validation", "Blocking diagnostics must be resolved before publication.", "validate", valid, true, valid ? "Validation passed" : validated ? "Validation found blocking errors" : "Validation has not run"),
                Step("runtime-audit", "Run the runtime audit", "Use the automated harness before asking another player to test the package.", "audit", audited, true, audited ? "Runtime audit acknowledged" : "Runtime audit not acknowledged", true),
                Step("build", "Build and package", "Generate the exact .ppmap revision that will be distributed.", "build_package", hasSuccessfulBuild, true, hasSuccessfulBuild ? "Package build succeeded" : "No successful package build"),
                Step("online", "Host or publish the package", "Only publish the exact package that passed the checks above.", "online", hasSuccessfulBuild && valid, false, hasSuccessfulBuild && valid ? "Ready for online preparation" : "Finish validation and packaging first")
            },
            _ => new List<StudioGuidanceStep>
            {
                Step("project", "Create an import project", "Map Studio needs an editable project to hold geometry, textures and generated assets.", "create_project", loaded, true, loaded ? "Project loaded" : "No project is open"),
                Step("save", "Save the project", "A saved project folder gives the importer a safe place for textures and generated files.", "save_project", saved, true, saved ? "Editable project has a file" : "Project has not been saved"),
                Step("import", "Import the 3D model", "Choose OBJ, glTF or GLB. Companion collision is detected beside the render model.", "import_model", imported, true, imported ? $"{map!.ModelSources.Count} model source(s)" : "No external model has been imported"),
                Step("orientation-reviewed", "Review scale and orientation", "Frame the whole model and confirm that up, scale, winding and UV direction are correct.", "frame_all", orientationReviewed, true, orientationReviewed ? "Orientation review acknowledged" : "Orientation still needs review", true),
                Step("collision", "Review or create collision", "Use source collision when available; otherwise choose a deliberate generated collision strategy.", "collision_review", collision, true, collision ? "Collision is present" : "Collision is missing"),
                Step("spawns", "Place safe player spawns", "Imported art is not playable until players have valid starting positions.", "add_spawn", spawns, true, spawns ? $"{map!.Spawns.Count} spawn(s)" : "No player spawns"),
                Step("gameplay-reviewed", "Review pickups and objectives", "Add only the gameplay objects required by the intended mode.", "add_pickup", gameplay, false, gameplay ? "Gameplay objects reviewed" : "Optional gameplay pass not completed", true),
                Step("materials", "Review materials and textures", "Confirm that textures resolve, UVs are readable and no fallback gray materials remain.", "materials", materials, false, materials ? $"{map!.Materials.Count} material(s)" : "No materials"),
                Step("validate", "Validate the imported map", "Resolve blocking diagnostics before compiling or packaging.", "validate", valid, true, valid ? "Validation has no blocking errors" : validated ? "Validation found blocking errors" : "Validation has not run"),
                Step("playtest", "Playtest traversal", "Test floors, ramps, tunnels, walls and alt-form routes in the actual runtime.", "playtest", playtested, true, playtested ? "Playtest acknowledged" : "Playtest not acknowledged", true),
                Step("build", "Build the .ppmap", "Create a distributable package after the map passes validation and traversal testing.", "build_package", hasSuccessfulBuild, true, hasSuccessfulBuild ? "Package build succeeded" : "No successful package build")
            }
        };

        string? next = steps.FirstOrDefault(step => step.Blocking && !step.Complete)?.Id
            ?? steps.FirstOrDefault(step => !step.Complete)?.Id;
        int complete = steps.Count(step => step.Complete);
        bool ready = saved && geometry && collision && spawns && valid && hasSuccessfulBuild;
        string summary = next == null
            ? "The selected workflow is complete. Keep the source project and package together with the test report."
            : $"{complete} of {steps.Count} steps complete. Next: {steps.First(step => step.Id == next).Title}.";
        return new(workflow, steps, next, complete, steps.Count, ready, summary);
    }

    private static StudioGuidanceStep Step(string id, string title, string summary, string action,
        bool complete, bool blocking, string status, bool mark = false)
        => new(id, title, summary, action, complete, blocking, status, mark);
}

/// <summary>Plain-language help used by beginner cards, tooltips and the searchable help catalog.</summary>
public static class StudioHelpCatalog
{
    private static readonly StudioHelpTopic[] _topics =
    {
        Topic("mode.beginner", "Beginner Mode", "Shows one recommended workflow at a time, hides specialist controls and explains the next safe action.",
            new[]{"Choose what you are trying to do.","Complete the highlighted next step.","Use the ? button whenever a term is unfamiliar.","Switch to Advanced Mode only when you need a specialist tool."},
            new[]{"Beginner Mode does not remove map capability; it reorganizes it.","A completed-looking map still needs validation and playtesting."}, "beginner guided wizard easy"),
        Topic("mode.advanced", "Advanced Mode", "Exposes the complete retained Map Studio workspace, including modeling, collision, materials, navigation, diagnostics and packaging.",
            new[]{"Use the scene hierarchy to select exact objects.","Choose object, face, edge or vertex mode.","Use overlays and inspectors to diagnose rather than guess.","Validate after structural changes."},
            new[]{"Do not edit collision blindly while only viewing rendered geometry.","Do not package a map with blocking diagnostics."}, "advanced expert all settings"),
        Topic("project.new", "New Project", "Creates an editable map project from a starter template.",
            new[]{"Choose the closest template.","Give the map a valid runtime name.","Save the project before importing textured models."},
            new[]{"A project is editable source; a .ppmap is the distributable package.","Do not overwrite your only working copy."}, "new template project"),
        Topic("project.open", "Open Project", "Opens an editable JSON project or a supported package for inspection.",
            new[]{"Open the project file.","Check the source and recovery messages.","Save a working copy before large changes."},
            new[]{"Loose source files may have moved since the last edit.","A packaged map may need an editable copy."}, "open load project"),
        Topic("project.save", "Save Project", "Writes the editable project and gives imported assets a stable project directory.",
            new[]{"Choose a dedicated folder.","Use map.json as the project file when sharing a project folder.","Keep source models beside their referenced dependencies."},
            new[]{"Saving is not the same as building a .ppmap.","Moving only the JSON can break relative asset paths."}, "save file folder"),
        Topic("import.model", "Import 3D Model", "Imports OBJ, glTF or GLB through the shared model pipeline while retaining materials and UV0.",
            new[]{"Choose the render model, not only its filename text.","Verify scale and source up-axis.","Choose a deliberate collision mode.","Preview the model, then apply it to the project."},
            new[]{"OBJ needs its MTL and textures in reachable relative paths.","A render mesh is not automatically good collision.","Do not repeatedly click Preview while an import is running."}, "obj gltf glb universal import"),
        Topic("import.scale", "Import Scale", "Multiplies source coordinates before they enter Project Prime units.",
            new[]{"Start at 1.","Preview and compare doors, player height and route widths.","Change scale once at the source import level rather than scaling many objects later."},
            new[]{"Very small or large values can create precision and collision problems."}, "scale units size"),
        Topic("import.zup", "Z-Up Source", "Converts models authored with Z as the vertical axis into Project Prime's coordinate convention.",
            new[]{"Enable for many Blender and 3ds Max exports when the preview lies on its side.","Disable when the source is already upright."},
            new[]{"Do not compensate for a wrong up-axis by rotating hundreds of imported objects."}, "z up axis blender max"),
        Topic("import.winding", "Reverse Face Winding", "Reverses triangle orientation when visible surfaces face inward or are culled from the expected side.",
            new[]{"Leave off initially.","Enable only when the preview shows inside-out surfaces."},
            new[]{"Winding is not a substitute for valid normals or closed collision."}, "winding faces normals inside out"),
        Topic("import.uv", "Flip UV Vertically", "Flips the V texture coordinate for formats or exporters whose texture origin differs.",
            new[]{"Compare recognizable text or directional textures in the preview.","Toggle only when textures are vertically inverted."},
            new[]{"This does not fix arbitrary stretching or missing UVs."}, "uv texture flip vertical"),
        Topic("collision.companion", "Companion Collision", "Uses a separately authored collision file beside the visible model, such as name_collision.obj.",
            new[]{"Keep render and collision files in the same folder.","Use the expected companion filename.","Enable the collision overlay before applying."},
            new[]{"Do not assume detection succeeded; read the companion status.","Collision must still be validated for gaps and blockers."}, "companion collision separate mesh"),
        Topic("collision.review", "Collision Review", "Shows the surfaces used for movement and projectile containment instead of only the visible art.",
            new[]{"Enable the collision overlay.","Check floors, walls, ramps, openings and tunnel clearance.","Use normals and heat views for suspicious areas.","Compile and playtest after edits."},
            new[]{"Invisible blockers can be correct, but must be intentional.","Decorative render triangles often make poor collision."}, "collision overlay solid ramp gap"),
        Topic("geometry.blockout", "Geometry Blockout", "Builds the playable layout with simple, readable shapes before visual detailing.",
            new[]{"Create the main floor and boundaries.","Add primary routes and elevations.","Test player and alt-form clearances.","Only then add trim and decoration."},
            new[]{"Detailed art can hide broken routes.","Do not use thin decorative planes as structural collision."}, "blockout box wedge ramp layout"),
        Topic("gameplay.spawn", "Player Spawns", "Defines where players enter the map and which team may use each start.",
            new[]{"Place the spawn on valid floor.","Check headroom and side clearance.","Aim it toward a safe route.","Add enough starts for every supported mode."},
            new[]{"Do not place starts inside collision or over a drop.","Avoid immediate line-of-sight into enemy starts."}, "spawn start team headroom"),
        Topic("gameplay.pickup", "Pickups and Objectives", "Adds health, ammunition, weapons and mode-specific gameplay objects.",
            new[]{"Place them on reachable floor.","Use layout and mode goals to choose locations.","Check timers, symmetry and route risk."},
            new[]{"More pickups do not automatically improve flow.","Do not place required objectives in unreachable geometry."}, "pickup item objective weapon health"),
        Topic("materials", "Materials and Textures", "Controls surface appearance, texture sources, UV scale and supported animation channels.",
            new[]{"Resolve missing textures first.","Check UV density and orientation.","Use normal, emissive and other channels deliberately.","Inspect the result in OpenGL runtime."},
            new[]{"A gray fallback is a failure, not a finished material.","Large textures can increase package size without visible benefit."}, "material texture uv normal emissive"),
        Topic("validation", "Validation", "Runs deterministic checks on source structure, geometry, gameplay entities, assets and budgets.",
            new[]{"Run validation after imports and structural edits.","Select each problem to locate it.","Resolve errors before warnings when possible.","Revalidate after every repair."},
            new[]{"A successful save is not validation.","Do not ignore a blocking error because the viewport still renders."}, "validate errors warnings diagnostics"),
        Topic("compile", "Runtime Build", "Converts editable project data into Project Prime runtime structures without necessarily packaging a distributable file.",
            new[]{"Validate first.","Compile the current revision.","Review collision and budget diagnostics.","Run the runtime audit."},
            new[]{"A viewport preview can succeed while runtime compilation fails."}, "compile runtime build"),
        Topic("audit", "Runtime Audit", "Launches the automated runtime harness and records whether the map can load and execute basic checks.",
            new[]{"Compile successfully first.","Run the audit.","Read the final exit status and log tail.","Treat a crash or timeout as a blocking defect."},
            new[]{"The audit complements, but does not replace, movement playtesting."}, "audit automated test harness"),
        Topic("playtest", "Playtest", "Runs the map in the actual game so movement, collision, visibility and gameplay can be verified.",
            new[]{"Start at every spawn.","Traverse every route in both directions.","Test jumps, ramps, tunnels and alt forms.","Record exact locations of failures."},
            new[]{"Looking correct in the editor is not enough.","Do not test only the shortest route."}, "play test movement alt form"),
        Topic("export", "Build .ppmap", "Packages the verified map and required assets into the distributable Project Prime map format.",
            new[]{"Save the source project.","Pass validation and runtime checks.","Build the package.","Keep the build report with the package."},
            new[]{"Do not edit only the packaged output and lose the source project.","Do not distribute source-game assets without permission."}, "export package ppmap"),
        Topic("ai.engineer", "AI Map Engineer", "Exposes controlled inspection, validation and reversible editing tools to an external or in-client agent.",
            new[]{"Inspect before modifying.","Create or rely on an automatic checkpoint.","Make one evidence-based change.","Validate, compile and retain or revert."},
            new[]{"The AI should not make blind changes.","A tool result is not proof until the relevant validator or runtime test passes."}, "ai engineer agent checkpoint repair"),
        Topic("navigation", "Navigation", "Builds and inspects reachability data used for editor analysis and automated route tests.",
            new[]{"Generate navigation after collision is stable.","Inspect disconnected regions.","Use different agent profiles for different body sizes."},
            new[]{"Navigation cannot repair missing collision.","One agent profile cannot represent every alt form."}, "nav navigation reachability bots"),
        Topic("online", "Online Preparation", "Packages and hosts the exact validated revision intended for multiplayer use.",
            new[]{"Confirm supported modes and spawns.","Validate and audit.","Build one deterministic package.","Distribute that exact package to all participants."},
            new[]{"Do not spoof protocol versions.","Do not host a different local revision from the distributed package."}, "online host multiplayer publish")
    };

    public static IReadOnlyList<StudioHelpTopic> All => _topics;

    public static StudioHelpTopic Get(string id)
        => _topics.FirstOrDefault(topic => topic.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
            ?? new StudioHelpTopic(id, id, "No detailed explanation has been written for this control yet.",
                Array.Empty<string>(), Array.Empty<string>(), id);

    public static StudioHelpTopic? FindForButton(string title)
    {
        string key = title.Trim().ToLowerInvariant();
        if (key.Contains("import")) return Get("import.model");
        if (key.Contains("validate")) return Get("validation");
        if (key.Contains("audit")) return Get("audit");
        if (key.Contains("playtest") || key == "play") return Get("playtest");
        if (key.Contains("build") || key.Contains("export")) return Get("export");
        if (key.Contains("spawn")) return Get("gameplay.spawn");
        if (key.Contains("pickup") || key.Contains("item")) return Get("gameplay.pickup");
        if (key.Contains("material") || key.Contains("texture")) return Get("materials");
        if (key.Contains("collision")) return Get("collision.review");
        if (key.Contains("navigation")) return Get("navigation");
        if (key.Contains("ai engineer")) return Get("ai.engineer");
        if (key.Contains("new")) return Get("project.new");
        if (key.Contains("open") || key.Contains("library")) return Get("project.open");
        if (key.Contains("save")) return Get("project.save");
        return null;
    }

    private static StudioHelpTopic Topic(string id, string title, string summary,
        string[] steps, string[] mistakes, string search)
        => new(id, title, summary, steps, mistakes, search);
}
