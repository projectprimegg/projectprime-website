using System;
using System.Linq;

namespace MphRead.Mods.MapGen;

/// <summary>
/// Optional hard build gate for maps that claim collision certification. When
/// enabled, compilation audits final packed collision rather than trusting the
/// visual source or an authoring preview.
/// </summary>
public sealed class MapCollisionCertificationSettings
{
    public bool Enabled { get; set; } = true;
    public bool RequireAllHunterAltForms { get; set; } = true;
    public bool RequireZeroProbeFailures { get; set; } = true;
    public bool RequireZeroSweepFailures { get; set; } = true;
    public bool RequireZeroContactOverflow { get; set; } = true;
    public bool RequireSafeSpawns { get; set; } = true;
    public bool RequireCleanTopology { get; set; } = true;
    public bool RequireConnectedTraversal { get; set; }
}

public static class MapCollisionCertification
{
    public static void Validate(MapDefinition definition, BuiltMap map, MapValidationResult result)
    {
        MapCollisionCertificationSettings? settings = definition.CollisionCertification;
        if (settings?.Enabled != true) return;
        MapCollisionHealth? health = map.CollisionHealth;
        if (health == null)
        {
            result.Error("FP-COLLISION-001", "Collision certification did not produce an audit report.");
            return;
        }

        int overflows = map.CollisionRepairs.Count(repair =>
            repair.Kind == MapCollisionRepairKind.ContactOverflowRisk);
        if (settings.RequireSafeSpawns
            && (health.SpawnChecks != definition.Spawns.Count || health.SpawnFailures != 0))
            result.Error("FP-COLLISION-001",
                $"Collision certification rejected {health.SpawnFailures:N0} of {health.SpawnChecks:N0} spawn clearance checks.");
        if (settings.RequireZeroProbeFailures && health.ProbeFailures != 0)
            result.Error("FP-COLLISION-001",
                $"Collision certification found {health.ProbeFailures:N0} unsupported rendered-floor probes.");
        if (settings.RequireZeroSweepFailures && health.SweepFailures != 0)
            result.Error("FP-COLLISION-001",
                $"Collision certification found {health.SweepFailures:N0} biped traversal or jump-path failures.");
        if (settings.RequireAllHunterAltForms
            && (health.AltSweepChecks == 0 || health.AltSweepFailures != 0))
            result.Error("FP-COLLISION-001",
                health.AltSweepChecks == 0
                    ? "Collision certification could not exercise all hunter alt forms."
                    : $"Collision certification found {health.AltSweepFailures:N0} alt-form clearance failures across {health.AltSweepChecks:N0} checks.");
        if (settings.RequireZeroContactOverflow && overflows != 0)
            result.Error("FP-COLLISION-001",
                $"Collision certification found {overflows:N0} places capable of saturating the runtime contact buffer.");
        if (settings.RequireCleanTopology
            && health.DegenerateFacesRemoved + health.OverlappingFaces + health.WindingWarnings != 0)
            result.Error("FP-COLLISION-001",
                $"Collision certification found topology defects: {health.DegenerateFacesRemoved:N0} degenerate, "
                + $"{health.OverlappingFaces:N0} overlapping, {health.WindingWarnings:N0} winding.");
        if (settings.RequireConnectedTraversal
            && (health.NavigationNodes == 0
                || health.NavigationComponents != health.ReachableComponents))
            result.Error("FP-COLLISION-001",
                health.NavigationNodes == 0
                    ? "Collision certification could not build a traversal graph."
                    : $"Collision certification found {health.NavigationComponents:N0} traversal components, "
                        + $"but spawns reach {health.ReachableComponents:N0}.");

        if (result.IsValid)
            result.Diagnostics.Add(new("FP-COLLISION-000", MapDiagnosticSeverity.Info,
                $"Certified final collision: {health.SpawnChecks:N0} spawns, "
                + $"{health.ProbeCount:N0} floor probes, {health.SweepCount:N0} biped sweeps and "
                + $"{health.AltSweepChecks:N0} all-hunter alt-form checks passed."));
    }
}
