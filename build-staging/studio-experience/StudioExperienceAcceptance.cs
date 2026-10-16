using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.MapEditor;

public sealed record StudioAcceptanceCheck(
    string Name,
    bool Passed,
    string Detail,
    double Milliseconds);

public sealed record StudioAcceptanceReport(
    bool Passed,
    int PassedChecks,
    int TotalChecks,
    string Summary,
    string Source,
    string CreatedUtc,
    IReadOnlyList<StudioAcceptanceCheck> Checks);

/// <summary>
/// Deterministic, user-free acceptance runner for the Studio infrastructure.
/// A generated OBJ/MTL and companion collision file exercise the same importer,
/// project application, validator, compiler, guidance and persistence paths as
/// an external map without redistributing any source-game assets.
/// </summary>
public static class StudioExperienceAcceptance
{
    public static StudioAcceptanceReport Run(
        string? sourceModel,
        string? outputPath,
        CancellationToken cancellation = default)
    {
        var checks = new List<StudioAcceptanceCheck>();
        string root = Path.Combine(Path.GetTempPath(),
            "project-prime-studio-acceptance-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string previousUserMaps = CustomRooms.UserMapDirectory;
        try
        {
            CustomRooms.UserMapDirectory = Path.Combine(root, "user-maps");
            Directory.CreateDirectory(CustomRooms.UserMapDirectory);
            string modelPath = sourceModel == null
                ? WriteFixture(root)
                : Path.GetFullPath(sourceModel);
            if (!File.Exists(modelPath))
                throw new FileNotFoundException("Acceptance source model was not found.", modelPath);

            ImportedModel? imported = null;
            var settings = new ModelImportSettings(1, false, false, true, false,
                ModelCollisionMode.Companion);
            Check(checks, "Source model is readable", () =>
            {
                using var stream = File.OpenRead(modelPath);
                return (stream.Length > 0, $"{Path.GetFileName(modelPath)} · {stream.Length} bytes");
            });

            Check(checks, "Companion collision is detected", () =>
            {
                string? companion = ModelCollisionImport.FindCompanion(modelPath);
                return (companion != null && File.Exists(companion),
                    companion == null ? "No companion collision file was detected."
                        : Path.GetFileName(companion));
            });

            Check(checks, "Model import completes before timeout", () =>
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                timeout.CancelAfter(TimeSpan.FromSeconds(90));
                imported = ModelImportService.Import(modelPath, settings, timeout.Token);
                return (imported.Meshes.Count > 0,
                    $"{imported.Meshes.Count} mesh(es), {imported.Materials.Count} material(s), {imported.Warnings.Count} warning(s)");
            });

            Check(checks, "Render geometry is nonempty", () =>
            {
                int meshes = imported?.Meshes.Count(mesh => !mesh.CollisionOnly) ?? 0;
                int faces = imported?.Meshes.Where(mesh => !mesh.CollisionOnly).Sum(mesh => mesh.Faces.Count) ?? 0;
                return (meshes > 0 && faces > 0, $"{meshes} render mesh(es), {faces} face(s)");
            });

            Check(checks, "Companion collision geometry is nonempty", () =>
            {
                int meshes = imported?.Meshes.Count(mesh => mesh.CollisionOnly) ?? 0;
                int faces = imported?.Meshes.Where(mesh => mesh.CollisionOnly).Sum(mesh => mesh.Faces.Count) ?? 0;
                bool solid = imported?.Meshes.Where(mesh => mesh.CollisionOnly).All(mesh => mesh.Solid) == true;
                return (meshes > 0 && faces > 0 && solid,
                    $"{meshes} collision mesh(es), {faces} face(s), solid={solid}");
            });

            Check(checks, "Imported coordinates and indices are finite", () =>
            {
                if (imported == null) return (false, "Import did not produce a model.");
                foreach (MapMesh mesh in imported.Meshes)
                {
                    foreach (float[] vertex in mesh.Vertices)
                        if (vertex.Length != 3 || vertex.Any(value => !Single.IsFinite(value)))
                            return (false, $"Invalid vertex in {mesh.Label}.");
                    foreach (int[] face in mesh.Faces)
                        if (face.Length < 3 || face.Any(index => index < 0 || index >= mesh.Vertices.Count))
                            return (false, $"Invalid face index in {mesh.Label}.");
                }
                return (true, "All imported vertices and face indices are finite and in range.");
            });

            MapDefinition? definition = null;
            MapValidationResult? validation = null;
            MapCompilation? compilation = null;
            Check(checks, "Imported model applies to an editable project", () =>
            {
                if (imported == null) return (false, "Import did not produce a model.");
                definition = MapTemplates.Create("STUDIO_ACCEPTANCE", "blank-arena").Definition;
                definition.BaseDirectory = root;

                string prefix = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
                foreach (KeyValuePair<string, byte[]> asset in imported.Assets)
                {
                    MapPackageReader.CanonicalName(asset.Key);
                    string destination = Path.GetFullPath(Path.Combine(root, asset.Key));
                    if (!destination.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Imported acceptance asset escapes its project directory.");
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.WriteAllBytes(destination, asset.Value);
                }

                ModelReimport.Apply(definition, imported, modelPath,
                    MapSourceFingerprint.Hash(imported.Dependencies), settings);
                bool applied = definition.ModelSources.Count == 1
                    && definition.Geometry.OfType<MapMesh>().Any();
                return (applied,
                    $"{definition.Geometry.Count} geometry object(s), {definition.ModelSources.Count} tracked source(s), {imported.Assets.Count} staged asset(s)");
            });

            Check(checks, "Project source validation runs", () =>
            {
                if (definition == null) return (false, "Project application did not run.");
                validation = MapValidator.Validate(definition, checkSources: true);
                int errors = validation.Diagnostics.Count(item => item.Severity == MapDiagnosticSeverity.Error);
                return (errors == 0,
                    $"{errors} error(s), {validation.Diagnostics.Count} total diagnostic(s)");
            });

            Check(checks, "Runtime compilation produces collision", () =>
            {
                if (definition == null) return (false, "Project application did not run.");
                compilation = MapCompiler.Compile(definition, cancellation);
                int faces = compilation.Map?.Solid.Count ?? 0;
                bool passed = compilation.Map != null && faces > 0
                    && compilation.Validation.Diagnostics.All(d => d.Severity != MapDiagnosticSeverity.Error);
                string errors = String.Join(" | ", compilation.Validation.Diagnostics
                    .Where(d => d.Severity == MapDiagnosticSeverity.Error)
                    .Take(8)
                    .Select(d => $"{d.Code}: {d.Message}"));
                string detail = $"runtime map={compilation.Map != null}, collision faces={faces}";
                if (errors.Length > 0) detail += " · " + errors;
                return (passed, detail);
            });

            Check(checks, "Beginner workflow recommends a real next step", () =>
            {
                if (definition == null) return (false, "Project application did not run.");
                var acknowledged = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "orientation-reviewed"
                };
                StudioGuidanceSnapshot guidance = StudioExperienceEngine.Evaluate(
                    definition,
                    Path.Combine(root, "map.json"),
                    validation,
                    compilation?.Map != null,
                    StudioWorkflow.ImportExternalModel,
                    acknowledged);
                bool passed = guidance.Steps.Count >= 8
                    && guidance.Steps.Any(step => step.Id == "import" && step.Complete)
                    && guidance.NextStepId != "project"
                    && guidance.NextStepId != "save"
                    && guidance.NextStepId != "import";
                return (passed,
                    $"{guidance.CompleteCount}/{guidance.TotalCount} complete · next={guidance.NextStepId ?? "none"}");
            });

            Check(checks, "Advanced and beginner preferences persist", () =>
            {
                if (definition == null) return (false, "Project application did not run.");
                var state = new MapStudioState
                {
                    ExperienceMode = StudioExperienceMode.Advanced.ToString(),
                    GuidedWorkflow = StudioWorkflow.PrepareForOnline.ToString()
                };
                state.CompletedGuideSteps.Add("orientation-reviewed");
                MapStudioStateStore.Save(definition, state);
                MapStudioState loaded = MapStudioStateStore.Load(definition);
                bool passed = loaded.ExperienceMode == "Advanced"
                    && loaded.GuidedWorkflow == "PrepareForOnline"
                    && loaded.CompletedGuideSteps.Contains("orientation-reviewed");
                return (passed,
                    $"mode={loaded.ExperienceMode}, workflow={loaded.GuidedWorkflow}, acknowledgements={loaded.CompletedGuideSteps.Count}");
            });

            Check(checks, "Help catalog covers core workflow actions", () =>
            {
                string[] required =
                {
                    "project.new", "project.save", "import.model", "collision.review",
                    "gameplay.spawn", "materials", "validation", "audit", "playtest", "export"
                };
                string[] missing = required.Where(id =>
                    StudioHelpCatalog.Get(id).Title == id).ToArray();
                return (missing.Length == 0,
                    missing.Length == 0 ? $"{required.Length} required topics present"
                        : "Missing: " + String.Join(", ", missing));
            });

            int passedChecks = checks.Count(check => check.Passed);
            var report = new StudioAcceptanceReport(
                passedChecks == checks.Count,
                passedChecks,
                checks.Count,
                passedChecks == checks.Count
                    ? "The self-contained Studio acceptance suite passed."
                    : "One or more Studio acceptance checks failed.",
                modelPath,
                DateTime.UtcNow.ToString("O"),
                checks);
            if (!String.IsNullOrWhiteSpace(outputPath))
            {
                string full = Path.GetFullPath(outputPath);
                Directory.CreateDirectory(Path.GetDirectoryName(full) ?? Directory.GetCurrentDirectory());
                File.WriteAllText(full, JsonSerializer.Serialize(report,
                    new JsonSerializerOptions { WriteIndented = true }));
            }
            return report;
        }
        finally
        {
            CustomRooms.UserMapDirectory = previousUserMaps;
            if (sourceModel == null)
            {
                try { if (Directory.Exists(root)) Directory.Delete(root, true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
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
            checks.Add(new(name, false,
                ex.GetType().Name + ": " + ex.Message, watch.Elapsed.TotalMilliseconds));
        }
    }

    private static string WriteFixture(string root)
    {
        string obj = Path.Combine(root, "studio_fixture.obj");
        string mtl = Path.Combine(root, "studio_fixture.mtl");
        string collision = Path.Combine(root, "studio_fixture_collision.obj");
        File.WriteAllText(mtl,
            "newmtl floor\nKd 0.45 0.55 0.65\n"
            + "newmtl wall\nKd 0.25 0.30 0.38\n");
        File.WriteAllText(obj,
            "mtllib studio_fixture.mtl\n"
            + "o room\n"
            + "v -4 0 -4\nv 4 0 -4\nv 4 0 4\nv -4 0 4\n"
            + "v -4 4 -4\nv 4 4 -4\n"
            + "vt 0 0\nvt 1 0\nvt 1 1\nvt 0 1\n"
            + "usemtl floor\nf 1/1 2/2 3/3 4/4\n"
            + "usemtl wall\nf 1/1 5/4 6/3 2/2\n");
        File.WriteAllText(collision,
            "o collision\n"
            + "v -4 0 -4\nv 4 0 -4\nv 4 0 4\nv -4 0 4\n"
            + "v -4 4 -4\nv 4 4 -4\n"
            + "f 1 2 3 4\nf 1 5 6 2\n");
        return obj;
    }
}

public static class StudioExperienceCheckCommand
{
    public static int Run(string[] args)
    {
        try
        {
            string? source = ValueAfter(args, "studioexperiencesource");
            string? output = ValueAfter(args, "studioexperienceoutput");
            StudioAcceptanceReport report = StudioExperienceAcceptance.Run(source, output);
            Console.WriteLine(JsonSerializer.Serialize(report,
                new JsonSerializerOptions { WriteIndented = true }));
            return report.Passed ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                passed = false,
                errorType = ex.GetType().Name,
                error = ex.Message
            }, new JsonSerializerOptions { WriteIndented = true }));
            return 1;
        }
    }

    private static string? ValueAfter(string[] args, string name)
    {
        string flag = "-" + name;
        for (int i = 0; i + 1 < args.Length; i++)
            if (args[i].Equals(flag, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        return null;
    }
}
