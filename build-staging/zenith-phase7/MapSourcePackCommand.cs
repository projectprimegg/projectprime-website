using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace MphRead.Mods.MapGen.Universal;

/// <summary>
/// Headless entry point for inspecting and validating structured external map
/// archives. It gives CI and AI Map Engineer a deterministic route through the
/// same adapter used by Map Studio, without requiring a user to operate dialogs.
/// </summary>
public static class MapSourcePackCommand
{
    private static readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static int Run(string[] args)
    {
        try
        {
            if (HasFlag(args, "sourcepackcheck"))
                return RunSelfCheck(ValueAfter(args, "sourcepackoutput"));

            string? archive = ValueAfter(args, "mapsourceinspect")
                ?? ValueAfter(args, "mapsourcevalidate");
            if (String.IsNullOrWhiteSpace(archive))
            {
                Console.Error.WriteLine(
                    "Usage: -mapsourceinspect <archive.zip> [-mapsourcereport report.json]\n"
                    + "       -mapsourcevalidate <archive.zip> [-mapsourcereport report.json] "
                    + "[-mapsourcescale n] [-mapsourcezup] [-mapsourceflipwinding] "
                    + "[-mapsourcenoflipuv] [-mapsourcecollision None|Visual|Simplified|BoundingBoxes|Companion]\n"
                    + "       -sourcepackcheck [-sourcepackoutput report.json]");
                return 2;
            }

            archive = Path.GetFullPath(archive);
            SourceArchiveInspection inspection = SourceArchiveInspector.Inspect(archive);
            if (HasFlag(args, "mapsourceinspect"))
            {
                string json = SourceArchiveInspector.ToJson(inspection);
                WriteReport(json, ValueAfter(args, "mapsourcereport"));
                return inspection.SelectedRenderModels.Count > 0 ? 0 : 1;
            }

            ModelCollisionMode collision = ParseCollision(
                ValueAfter(args, "mapsourcecollision"),
                inspection.SelectedCollisionModels.Count > 0
                    ? ModelCollisionMode.Companion
                    : ModelCollisionMode.None);
            float scale = ParseFloat(ValueAfter(args, "mapsourcescale"), 1f);
            bool flipUv = !HasFlag(args, "mapsourcenoflipuv");
            var options = new UniversalImportOptions(
                Scale: scale,
                FlipWinding: HasFlag(args, "mapsourceflipwinding"),
                ZUp: HasFlag(args, "mapsourcezup"),
                FlipUvVertical: flipUv,
                Collision: collision);

            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(20));
            var adapter = new SourceArchiveUniversalAdapter();
            UniversalImportResult imported = adapter.Import(archive, options, timeout.Token);
            IReadOnlyList<UniversalDiagnostic> diagnostics = UniversalMapValidator.Validate(imported.Map);
            imported.Map.Diagnostics.AddRange(diagnostics);
            ImportedModel projectPrime = UniversalMapProjectPrimeBackend.ToImportedModel(imported);
            UniversalDiagnostic[] errors = imported.Map.Diagnostics
                .Where(item => item.Severity.Equals("error", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            int renderFaces = imported.Map.RenderMeshes.Sum(mesh => mesh.Faces.Count);
            int collisionFaces = imported.Map.CollisionMeshes.Sum(mesh => mesh.Faces.Count);
            bool passed = errors.Length == 0
                && imported.Map.RenderMeshes.Count > 0
                && renderFaces > 0
                && (collision == ModelCollisionMode.None || collisionFaces > 0)
                && projectPrime.Meshes.Count > 0;

            string validationJson = JsonSerializer.Serialize(new
            {
                passed,
                mode = "validate",
                archive,
                adapter = adapter.DisplayName,
                fidelityTier = adapter.FidelityTier,
                selectedRenderModels = inspection.SelectedRenderModels,
                selectedCollisionModels = inspection.SelectedCollisionModels,
                settings = options,
                universalMap = new
                {
                    renderMeshes = imported.Map.RenderMeshes.Count,
                    renderFaces,
                    collisionMeshes = imported.Map.CollisionMeshes.Count,
                    collisionFaces,
                    materials = imported.Map.Materials.Count,
                    entities = imported.Map.Entities.Count,
                    assets = imported.Assets.Count,
                    dependencies = imported.Dependencies.Count
                },
                projectPrimeBackend = new
                {
                    meshes = projectPrime.Meshes.Count,
                    materials = projectPrime.Materials.Count,
                    assets = projectPrime.Assets.Count
                },
                diagnostics = imported.Map.Diagnostics,
                inspection.Warnings
            }, _json);
            WriteReport(validationJson, ValueAfter(args, "mapsourcereport"));
            return passed ? 0 : 1;
        }
        catch (Exception ex)
        {
            string json = JsonSerializer.Serialize(new
            {
                passed = false,
                errorType = ex.GetType().Name,
                error = ex.Message,
                stack = ex.StackTrace
            }, _json);
            Console.Error.WriteLine(json);
            string? report = ValueAfter(args, "mapsourcereport")
                ?? ValueAfter(args, "sourcepackoutput");
            if (!String.IsNullOrWhiteSpace(report))
            {
                try { WriteFile(Path.GetFullPath(report), json); }
                catch (Exception writeEx) { Console.Error.WriteLine("Could not write failure report: " + writeEx.Message); }
            }
            return 1;
        }
    }

    private static int RunSelfCheck(string? outputPath)
    {
        string root = Path.Combine(Path.GetTempPath(),
            "ProjectPrime-source-pack-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var checks = new List<object>();
        bool allPassed = true;
        void Check(string name, Func<(bool Passed, string Detail)> run)
        {
            var started = DateTime.UtcNow;
            try
            {
                (bool passed, string detail) = run();
                allPassed &= passed;
                checks.Add(new { name, passed, detail, milliseconds = (DateTime.UtcNow - started).TotalMilliseconds });
            }
            catch (Exception ex)
            {
                allPassed = false;
                checks.Add(new
                {
                    name,
                    passed = false,
                    detail = ex.GetType().Name + ": " + ex.Message,
                    milliseconds = (DateTime.UtcNow - started).TotalMilliseconds
                });
            }
        }

        try
        {
            string source = Path.Combine(root, "source");
            Directory.CreateDirectory(Path.Combine(source, "models"));
            File.WriteAllText(Path.Combine(source, "models", "zenith.obj"),
                "o ZenithWorld\n"
                + "v -4 0 -4\nv 4 0 -4\nv 4 0 4\nv -4 0 4\n"
                + "vt 0 0\nvt 1 0\nvt 1 1\nvt 0 1\n"
                + "f 1/1 2/2 3/3\nf 1/1 3/3 4/4\n", Encoding.UTF8);
            File.WriteAllText(Path.Combine(source, "models", "zenith_collision.obj"),
                "o ZenithCollision\n"
                + "v -4 0 -4\nv 4 0 -4\nv 4 0 4\nv -4 0 4\n"
                + "f 1 2 3\nf 1 3 4\n", Encoding.UTF8);
            File.WriteAllText(Path.Combine(source, "projectprime-source.json"),
                JsonSerializer.Serialize(new SourceArchiveManifest
                {
                    Name = "ZENITH_SOURCE_PACK_CHECK",
                    RenderModel = "models/zenith.obj",
                    CollisionModel = "models/zenith_collision.obj",
                    FlipUvVertical = false
                }, _json), Encoding.UTF8);
            string archive = Path.Combine(root, "zenith_source_pack.zip");
            ZipFile.CreateFromDirectory(source, archive, CompressionLevel.Optimal, includeBaseDirectory: false);

            SourceArchiveInspection? inspection = null;
            UniversalImportResult? imported = null;
            ImportedModel? backend = null;

            Check("Structured archive inspection selects manifest render and collision models", () =>
            {
                inspection = SourceArchiveInspector.Inspect(archive);
                bool passed = inspection.SelectedRenderModels.SequenceEqual(
                        new[] { "models/zenith.obj" }, StringComparer.OrdinalIgnoreCase)
                    && inspection.SelectedCollisionModels.SequenceEqual(
                        new[] { "models/zenith_collision.obj" }, StringComparer.OrdinalIgnoreCase);
                return (passed,
                    $"render={String.Join(',', inspection.SelectedRenderModels)}; collision={String.Join(',', inspection.SelectedCollisionModels)}");
            });

            Check("Tier-2 source-pack adapter produces separate render and collision geometry", () =>
            {
                imported = new SourceArchiveUniversalAdapter().Import(
                    archive,
                    new UniversalImportOptions(
                        Scale: 1,
                        FlipWinding: false,
                        ZUp: false,
                        FlipUvVertical: false,
                        Collision: ModelCollisionMode.Companion));
                int renderFaces = imported.Map.RenderMeshes.Sum(mesh => mesh.Faces.Count);
                int collisionFaces = imported.Map.CollisionMeshes.Sum(mesh => mesh.Faces.Count);
                bool passed = renderFaces == 2 && collisionFaces == 2
                    && imported.Map.CollisionMeshes.All(mesh => mesh.CollisionOnly && mesh.Solid);
                return (passed, $"renderFaces={renderFaces}; collisionFaces={collisionFaces}");
            });

            Check("UniversalMap validation and Project Prime backend accept the source pack", () =>
            {
                if (imported == null) return (false, "Adapter check did not run.");
                IReadOnlyList<UniversalDiagnostic> diagnostics = UniversalMapValidator.Validate(imported.Map);
                backend = UniversalMapProjectPrimeBackend.ToImportedModel(imported);
                int errors = diagnostics.Count(item => item.Severity.Equals("error", StringComparison.OrdinalIgnoreCase));
                bool passed = errors == 0
                    && backend.Meshes.Any(mesh => !mesh.CollisionOnly)
                    && backend.Meshes.Any(mesh => mesh.CollisionOnly);
                return (passed, $"errors={errors}; meshes={backend.Meshes.Count}; materials={backend.Materials.Count}");
            });

            Check("Archive traversal paths are rejected before extraction", () =>
            {
                string malicious = Path.Combine(root, "malicious.zip");
                using (ZipArchive zip = ZipFile.Open(malicious, ZipArchiveMode.Create))
                {
                    ZipArchiveEntry entry = zip.CreateEntry("../escape.obj");
                    using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
                    writer.Write("v 0 0 0\n");
                }
                try
                {
                    SourceArchiveInspector.Inspect(malicious);
                    return (false, "Unsafe path was accepted.");
                }
                catch (InvalidDataException ex)
                {
                    return (true, ex.Message);
                }
            });

            string report = JsonSerializer.Serialize(new
            {
                passed = allPassed,
                summary = allPassed
                    ? "Structured source-pack inspection, import, validation and backend conversion passed."
                    : "One or more structured source-pack checks failed.",
                createdUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                checks
            }, _json);
            WriteReport(report, outputPath);
            return allPassed ? 0 : 1;
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static void WriteReport(string json, string? path)
    {
        Console.WriteLine(json);
        if (!String.IsNullOrWhiteSpace(path)) WriteFile(Path.GetFullPath(path), json);
    }

    private static void WriteFile(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static ModelCollisionMode ParseCollision(string? value, ModelCollisionMode fallback)
        => Enum.TryParse(value, ignoreCase: true, out ModelCollisionMode parsed)
            ? parsed : fallback;

    private static float ParseFloat(string? value, float fallback)
        => Single.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed)
            && Single.IsFinite(parsed) && parsed > 0
            ? parsed : fallback;

    private static bool HasFlag(string[] args, string flag) => IndexOfFlag(args, flag) >= 0;

    private static int IndexOfFlag(string[] args, string flag)
    {
        string normalized = flag.TrimStart('-');
        return Array.FindIndex(args, item =>
            item.TrimStart('-').Equals(normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static string? ValueAfter(string[] args, string flag)
    {
        int index = IndexOfFlag(args, flag);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
