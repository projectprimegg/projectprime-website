using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MphRead.Mods.MapEditor;

using MphRead.Mods.MapGen;

public sealed record ZenithPhase6Check(string Name, bool Passed, string Detail, double Milliseconds);
public sealed record ZenithPhase6Report(bool Passed, int PassedChecks, int TotalChecks,
    string Summary, IReadOnlyList<ZenithPhase6Check> Checks);

/// <summary>Deterministic checks for compact geometry and collision certification.</summary>
public static class ZenithPhase6Acceptance
{
    public static ZenithPhase6Report Run(string? projectPath = null)
    {
        var checks = new List<ZenithPhase6Check>();
        Check(checks, "Packed mesh round-trips exact geometry, materials and UVs", () =>
        {
            var mesh = new MapMesh
            {
                Material = 0,
                Solid = false,
                Vertices = new()
                {
                    new[] { -1f, 0f, -1f }, new[] { 1f, 0f, -1f },
                    new[] { 1f, 0f, 1f }, new[] { -1f, 0f, 1f }
                },
                Faces = new() { new[] { 0, 1, 2 }, new[] { 0, 2, 3 } },
                FaceMaterials = new() { 2, 3 },
                FaceTexcoords = new()
                {
                    new[] { new[] { 0f, 0f }, new[] { 64f, 0f }, new[] { 64f, 64f } },
                    new[] { new[] { 0f, 0f }, new[] { 64f, 64f }, new[] { 0f, 64f } }
                }
            };
            string encoded = MapPackedMeshCodec.Encode(new[] { mesh });
            var packed = new MapPackedMesh { Data = encoded, Encoding = MapPackedMeshCodec.EncodingName };
            IReadOnlyList<MapMesh> decoded = MapPackedMeshCodec.Decode(packed);
            IReadOnlyList<BuiltFace> compiled = GeometryCompiler.Compile(packed, 16);
            bool passed = decoded.Count == 1 && decoded[0].Vertices.Count == 4
                && decoded[0].Faces.Count == 2 && compiled.Count == 2
                && compiled[0].Material == 2 && compiled[1].Material == 3
                && Math.Abs(compiled[0].Texcoords[1].X - 64) < .001f;
            return (passed, $"chunks={decoded.Count}, faces={compiled.Count}, encoded={encoded.Length:N0} chars");
        });

        Check(checks, "Certified open arena passes biped and all-hunter alt-form audits", () =>
        {
            MapDefinition definition = SyntheticArena();
            MapCompilation compilation = MapCompiler.Compile(definition);
            MapCollisionHealth? health = compilation.Map?.CollisionHealth;
            string errors = String.Join(" | ", compilation.Validation.Diagnostics
                .Where(d => d.Severity == MapDiagnosticSeverity.Error)
                .Select(d => d.Code + ": " + d.Message));
            bool passed = compilation.Map != null && compilation.Validation.IsValid
                && health != null && health.SpawnFailures == 0
                && health.AltSweepChecks > 0 && health.AltSweepFailures == 0;
            return (passed, passed
                ? $"spawns={health!.SpawnChecks}, biped sweeps={health.SweepCount}, alt checks={health.AltSweepChecks}"
                : errors);
        });

        Check(checks, "Certification rejects an obstructed alt-form spawn", () =>
        {
            MapDefinition definition = SyntheticArena();
            definition.Geometry.Add(new MapBox
            {
                Label = "Low obstruction",
                Material = 0,
                Transform = new()
                {
                    Position = new[] { -8f, .62f, -8f },
                    Scale = new[] { 2f, .12f, 2f }
                }
            });
            MapCompilation compilation = MapCompiler.Compile(definition);
            bool rejected = !compilation.Validation.IsValid
                && compilation.Validation.Diagnostics.Any(d => d.Code == "FP-COLLISION-001");
            return (rejected, rejected ? "Obstructed certified map was refused."
                : "Expected strict collision certification to reject the obstruction.");
        });

        if (!String.IsNullOrWhiteSpace(projectPath))
        {
            Check(checks, "Zenith production project compiles within runtime budgets", () =>
            {
                string full = Path.GetFullPath(projectPath);
                MapDefinition definition = MapDefinition.Load(full);
                MapCompilation compilation = MapCompiler.Compile(definition);
                MapCollisionHealth? health = compilation.Map?.CollisionHealth;
                string errors = String.Join(" | ", compilation.Validation.Diagnostics
                    .Where(d => d.Severity == MapDiagnosticSeverity.Error)
                    .Take(20).Select(d => d.Code + ": " + d.Message));
                bool passed = compilation.Map != null && compilation.Validation.IsValid
                    && health != null && health.SpawnFailures == 0
                    && health.ProbeFailures == 0 && health.SweepFailures == 0
                    && health.AltSweepFailures == 0
                    && !compilation.Map.CollisionRepairs.Any(r =>
                        r.Kind == MapCollisionRepairKind.ContactOverflowRisk);
                return (passed, passed
                    ? $"faces={compilation.Map!.Faces.Count:N0}, collision={compilation.Map.Solid.Count:N0}, "
                        + $"spawns={health!.SpawnChecks}, floor={health.ProbeCount}, "
                        + $"biped={health.SweepCount}, alt={health.AltSweepChecks}"
                    : errors);
            });
        }

        int passedCount = checks.Count(check => check.Passed);
        return new(passedCount == checks.Count, passedCount, checks.Count,
            passedCount == checks.Count
                ? "Zenith Phase 6 compact geometry and collision certification passed."
                : $"Zenith Phase 6 failed {checks.Count - passedCount} check(s).",
            checks);
    }

    private static MapDefinition SyntheticArena()
    {
        var definition = new MapDefinition
        {
            FormatVersion = 2,
            MapId = Guid.Parse("9ba3ef7a-5514-48a0-a9af-31ee45b82eb4"),
            Name = "PHASE6 COLLISION CHECK",
            InGameName = "Phase 6 Collision Check",
            ScaleFactor = 4,
            KillHeight = -8,
            FarClip = 120,
            FogEnabled = false,
            Materials = new()
            {
                new MapMaterial { Id = Guid.Parse("d1020aa8-2118-4871-a6ac-3cc0f81dce87"),
                    Name = "Test", SourceMaterial = 2, TexScale = 16 }
            },
            Capabilities = new() { SupportedModes = new() { "Battle" } },
            CollisionCertification = new()
            {
                Enabled = true,
                RequireAllHunterAltForms = true,
                RequireZeroProbeFailures = true,
                RequireZeroSweepFailures = true,
                RequireZeroContactOverflow = true,
                RequireSafeSpawns = true,
                RequireCleanTopology = true,
                RequireConnectedTraversal = false
            }
        };
        definition.Geometry.Add(new MapBox
        {
            Label = "Certified floor", Material = 0,
            Transform = new() { Position = new[] { 0f, -.5f, 0f }, Scale = new[] { 40f, 1f, 40f } }
        });
        float[][] positions =
        {
            new[] { -8f, .08f, -8f }, new[] { 8f, .08f, -8f },
            new[] { 8f, .08f, 8f }, new[] { -8f, .08f, 8f }
        };
        for (int i = 0; i < positions.Length; i++)
            definition.Spawns.Add(new MapSpawn { Id = Guid.NewGuid(), Position = positions[i], Team = -1 });
        return definition;
    }

    private static void Check(List<ZenithPhase6Check> checks, string name,
        Func<(bool Passed, string Detail)> run)
    {
        var timer = Stopwatch.StartNew();
        try
        {
            (bool passed, string detail) = run();
            checks.Add(new(name, passed, detail, timer.Elapsed.TotalMilliseconds));
        }
        catch (Exception ex)
        {
            checks.Add(new(name, false, ex.GetType().Name + ": " + ex.Message,
                timer.Elapsed.TotalMilliseconds));
        }
    }
}

public static class ZenithPhase6CheckCommand
{
    public static int Run(string[] args)
    {
        string? project = ValueAfter(args, "zenithproject");
        string? output = ValueAfter(args, "zenithphase6output");
        ZenithPhase6Report report = ZenithPhase6Acceptance.Run(project);
        string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine(json);
        if (!String.IsNullOrWhiteSpace(output))
        {
            string full = Path.GetFullPath(output);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, json);
        }
        return report.Passed ? 0 : 1;
    }

    private static string? ValueAfter(string[] args, string flag)
    {
        string normalized = flag.TrimStart('-');
        int index = Array.FindIndex(args, item =>
            item.TrimStart('-').Equals(normalized, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
