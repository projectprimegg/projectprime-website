using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;

namespace MphRead.Mods.MapGen;

/// <summary>
/// Headless, deterministic map compile/certification/package entry point used by
/// CI and the AI Map Engineer. It has no editor or renderer dependency and never
/// packages a map whose source, runtime build, or all-player-form gate fails.
/// </summary>
public static class MapAuthoringCommand
{
    public static int Run(string[] args)
    {
        try
        {
            int compileAt = IndexOfFlag(args, "mapcompile");
            int packageAt = IndexOfFlag(args, "mappackage");
            int at = compileAt >= 0 ? compileAt : packageAt;
            if (at < 0 || at + 1 >= args.Length)
            {
                Console.Error.WriteLine("Usage: -mapcompile <project.json> [-mapreport <report.json>] OR -mappackage <project.json> [-mapoutput <map.ppmap>] [-mapreport <report.json>]");
                return 2;
            }

            string projectPath = Path.GetFullPath(args[at + 1]);
            MapDefinition definition = MapDefinition.Load(projectPath);
            MapCompilation compilation = MapCompiler.Compile(definition, CancellationToken.None);
            MapAltFormCertificationReport? certification = compilation.Map?.AltFormCertification;
            var report = new
            {
                project = projectPath,
                map = definition.Name,
                valid = compilation.Validation.IsValid && compilation.Map != null,
                diagnostics = compilation.Validation.Diagnostics.Select(d => new
                {
                    d.Code,
                    severity = d.Severity.ToString(),
                    d.Message,
                    d.ObjectId
                }).ToArray(),
                budgets = compilation.Validation.Budgets.Select(b => new
                {
                    b.Name,
                    b.Used,
                    b.Limit,
                    b.Percent
                }).ToArray(),
                packedScene = compilation.Map?.PackedSceneSummary,
                playerForms = certification == null ? null : new
                {
                    certification.Passed,
                    certification.Profiles,
                    certification.Segments,
                    certification.Samples,
                    certification.MaximumContacts,
                    certification.Summary,
                    failures = certification.Failures.Select(f => new
                    {
                        f.Form,
                        f.Kind,
                        start = new[] { f.Start.X, f.Start.Y, f.Start.Z },
                        failure = new[] { f.Failure.X, f.Failure.Y, f.Failure.Z },
                        end = new[] { f.End.X, f.End.Y, f.End.Z },
                        f.Contacts,
                        f.Detail
                    }).ToArray()
                }
            };
            string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
            Console.WriteLine(json);
            WriteReport(args, json);
            if (!compilation.Validation.IsValid || compilation.Map == null) return 1;

            if (packageAt >= 0)
            {
                string output = ValueAfter(args, "mapoutput")
                    ?? Path.ChangeExtension(projectPath, ".ppmap");
                output = Path.GetFullPath(output);
                string built = MapPackageBuilder.Build(definition, output);
                using var reader = new MapPackageReader(built);
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    packaged = built,
                    bytes = new FileInfo(built).Length,
                    manifest = reader.Manifest
                }, new JsonSerializerOptions { WriteIndented = true }));
            }
            return 0;
        }
        catch (Exception ex)
        {
            string json = JsonSerializer.Serialize(new
            {
                success = false,
                errorType = ex.GetType().Name,
                error = ex.Message,
                detail = ex.ToString()
            }, new JsonSerializerOptions { WriteIndented = true });
            Console.Error.WriteLine(json);
            try { WriteReport(args, json); } catch { }
            return 1;
        }
    }

    private static void WriteReport(string[] args, string json)
    {
        string? report = ValueAfter(args, "mapreport");
        if (String.IsNullOrWhiteSpace(report)) return;
        string full = Path.GetFullPath(report);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, json);
    }

    private static int IndexOfFlag(string[] args, string flag)
    {
        string normalized = flag.TrimStart('-');
        return Array.FindIndex(args, item => item.TrimStart('-').Equals(normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static string? ValueAfter(string[] args, string flag)
    {
        int index = IndexOfFlag(args, flag);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
