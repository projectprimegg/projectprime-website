using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenTK.Mathematics;

namespace MphRead.Mods.MapGen;

public static partial class MapCollisionHealer
{
    private sealed record AltFormProbe(string Name, float Radius, float CenterOffset);

    private static readonly AltFormProbe[] _allHunterAltForms = Metadata.PlayerValues
        .Select((values, index) =>
        {
            float radius = MathF.Max(.01f, Fixed.ToFloat(values.AltColRadius));
            // Grounded form conversion preserves the old collision bottom. If
            // authored AltColYPos is below the sphere radius, the runtime lifts
            // Position by the difference before solving collision.
            float center = MathF.Max(radius, Fixed.ToFloat(values.AltColYPos));
            byte hunterValue = checked((byte)index);
            string name = Enum.IsDefined(typeof(Hunter), hunterValue)
                ? ((Hunter)hunterValue).ToString() : "Hunter " + index;
            return new AltFormProbe(name, radius, center);
        }).ToArray();

    /// <summary>
    /// Audit the exact final collision that will be packed. This never silently
    /// changes geometry or gameplay entities; a certified map either passes or
    /// is refused by MapCollisionCertification.
    /// </summary>
    public static MapCollisionHealth Certify(BuiltMap map, MapDefinition definition,
        CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(definition);
        map.CollisionRepairs.Clear();
        var health = new MapCollisionHealth
        {
            InputFaces = map.Solid.Count,
            OutputFaces = map.Solid.Count
        };
        map.CollisionHealth = health;
        if (map.Solid.Count == 0) return health;

        var collision = new FaceIndex(map.Solid.SelectMany(MapPacker.CollisionParts), 4f);
        AuditTopology(map, health, cancellation);
        AuditCertifiedSpawns(map, definition, collision, health, cancellation);
        AuditReachabilityAndProbes(map, definition, collision, health, cancellation);
        if (definition.CollisionCertification?.RequireAllHunterAltForms == true)
        {
            AuditAllHunterTraversal(map, definition, collision, health, cancellation);
            AuditAllHunterSurfaceClearance(map, collision, health, cancellation);
            AuditAllHunterJumpPads(map, definition, collision, health, cancellation);
        }
        return health;
    }

    private static void AuditCertifiedSpawns(BuiltMap map, MapDefinition definition,
        FaceIndex collision, MapCollisionHealth health, CancellationToken cancellation)
    {
        foreach (MapSpawn spawn in definition.Spawns)
        {
            cancellation.ThrowIfCancellationRequested();
            health.SpawnChecks++;
            Vector3 origin = V(spawn.Position);
            if (!TryFloor(collision, origin + Vector3.UnitY * .75f, 5f, 1.5f, out float floor))
            {
                health.SpawnFailures++;
                Record(map, new(MapCollisionRepairKind.SpawnClearanceFailure, .99f,
                    "Certified spawn has no usable collision floor.", new[] { origin }));
                continue;
            }
            Vector3 feet = new(origin.X, floor + CapsuleSkin, origin.Z);
            bool bipedClear = ProbePlayerCapsule(collision, feet,
                out int contacts, out Vector3 obstruction);
            if (contacts >= RuntimePlayerContactLimit)
                Record(map, new(MapCollisionRepairKind.ContactOverflowRisk, .99f,
                    $"Spawn standing probe encountered {contacts} collision faces; runtime capacity is {RuntimePlayerContactLimit}.",
                    new[] { feet }));
            if (!bipedClear)
            {
                health.SpawnFailures++;
                Record(map, new(MapCollisionRepairKind.SpawnClearanceFailure, .99f,
                    "Certified spawn is obstructed for the standing biped capsule.",
                    new[] { origin, obstruction }));
                continue;
            }
            if (definition.CollisionCertification?.RequireAllHunterAltForms == true
                && !ProbeAllHunterAltForms(collision, feet, out AltFormProbe? failed,
                    out contacts, out obstruction))
            {
                health.SpawnFailures++;
                Record(map, new(MapCollisionRepairKind.SpawnClearanceFailure, .99f,
                    $"Certified spawn is obstructed for {failed!.Name} alt form (radius {failed.Radius:0.###}).",
                    new[] { origin, obstruction }));
            }
            if (contacts >= RuntimePlayerContactLimit)
                Record(map, new(MapCollisionRepairKind.ContactOverflowRisk, .99f,
                    $"Spawn alt-form probe encountered {contacts} collision faces; runtime capacity is {RuntimePlayerContactLimit}.",
                    new[] { feet }));
        }
    }

    private static void AuditAllHunterTraversal(BuiltMap map, MapDefinition definition,
        FaceIndex collision, MapCollisionHealth health, CancellationToken cancellation)
    {
        MapNodePacker.NavigationGraph? graph = null;
        try { graph = MapNodePacker.Analyze(map.Solid, definition.NavigationLinks, cancellation); }
        catch (MapAuthoringException) { }
        if (graph == null) return;

        int auditedEdges = 0;
        for (int a = 0; a < graph.Neighbours.Length && auditedEdges < 4000; a++)
        {
            foreach (int b in graph.Neighbours[a])
            {
                if (b <= a) continue;
                cancellation.ThrowIfCancellationRequested();
                Vector3 from = graph.Positions[a], to = graph.Positions[b];
                foreach (AltFormProbe probe in _allHunterAltForms)
                {
                    health.AltSweepChecks++;
                    if (!AuditAltSegment(collision, from, to, probe,
                        out Vector3 failure, out int maximumContacts))
                    {
                        health.AltSweepFailures++;
                        Record(map, new(MapCollisionRepairKind.AltFormSweepFailure, .99f,
                            $"{probe.Name} alt form cannot traverse this certified navigation edge "
                            + $"(radius {probe.Radius:0.###}, center offset {probe.CenterOffset:0.###}).",
                            new[] { from, failure, to }));
                    }
                    if (maximumContacts >= RuntimePlayerContactLimit)
                        Record(map, new(MapCollisionRepairKind.ContactOverflowRisk, .97f,
                            $"{probe.Name} alt-form sweep encountered {maximumContacts} collision faces; "
                            + $"runtime capacity is {RuntimePlayerContactLimit}.",
                            new[] { from, failure, to }));
                }
                auditedEdges++;
                if (auditedEdges >= 4000) break;
            }
        }
    }

    private static bool AuditAltSegment(FaceIndex collision, Vector3 from, Vector3 to,
        AltFormProbe probe, out Vector3 failurePoint, out int maximumContacts)
    {
        failurePoint = to;
        maximumContacts = 0;
        Vector3 delta = to - from;
        int steps = Math.Clamp((int)MathF.Ceiling(delta.Length / PlayerSweepStep), 1, 48);
        for (int step = 0; step <= steps; step++)
        {
            Vector3 sample = Vector3.Lerp(from, to, step / (float)steps);
            if (!TryFloor(collision, sample + Vector3.UnitY * .6f, 1.2f, .65f, out float floor))
            { failurePoint = sample; return false; }
            Vector3 feet = new(sample.X, floor + CapsuleSkin, sample.Z);
            bool clear = ProbeAltForm(collision, feet, probe,
                out int contacts, out Vector3 obstruction);
            maximumContacts = Math.Max(maximumContacts, contacts);
            if (!clear) { failurePoint = obstruction; return false; }
            failurePoint = feet;
        }
        return true;
    }

    private static void AuditAllHunterSurfaceClearance(BuiltMap map,
        FaceIndex collision, MapCollisionHealth health, CancellationToken cancellation)
    {
        int stride = Math.Max(1, map.Faces.Count / 2048);
        int sampled = 0;
        for (int i = 0; i < map.Faces.Count && sampled < 2048; i += stride)
        {
            cancellation.ThrowIfCancellationRequested();
            BuiltFace face = map.Faces[i];
            if (face.Sky || face.Normal.Y < WalkableY) continue;
            foreach (Vector3 sample in Samples(face).Take(1))
            {
                if (!TryFloor(collision, sample + Vector3.UnitY * .5f, 1.1f, .5f, out float floor))
                    continue;
                Vector3 feet = new(sample.X, floor + CapsuleSkin, sample.Z);
                foreach (AltFormProbe probe in _allHunterAltForms)
                {
                    health.AltSweepChecks++;
                    if (!ProbeAltForm(collision, feet, probe,
                        out int contacts, out Vector3 obstruction))
                    {
                        health.AltSweepFailures++;
                        Record(map, new(MapCollisionRepairKind.AltFormSweepFailure, .96f,
                            $"Rendered walkable surface lacks {probe.Name} alt-form clearance.",
                            new[] { feet, obstruction }));
                    }
                    if (contacts >= RuntimePlayerContactLimit)
                        Record(map, new(MapCollisionRepairKind.ContactOverflowRisk, .94f,
                            $"{probe.Name} standing alt-form probe encountered {contacts} collision faces.",
                            new[] { feet }));
                }
                sampled++;
                if (sampled >= 2048) break;
            }
        }
    }

    private static void AuditAllHunterJumpPads(BuiltMap map, MapDefinition definition,
        FaceIndex collision, MapCollisionHealth health, CancellationToken cancellation)
    {
        foreach (MapJumpPad pad in definition.JumpPads)
        {
            Vector3 start = V(pad.Position);
            Vector3 direction; float speed;
            try { (direction, speed) = MapBuilder.SolveJumpPad(pad); }
            catch (Exception ex) when (ex is ProgramException or ArgumentException)
            {
                foreach (AltFormProbe probe in _allHunterAltForms)
                { health.AltSweepChecks++; health.AltSweepFailures++; }
                continue;
            }
            Vector3 velocity = direction * speed;
            float duration = JumpPadDuration(pad, start, velocity);
            int frames = Math.Clamp((int)MathF.Ceiling(duration), 1, 240);
            foreach (AltFormProbe probe in _allHunterAltForms)
            {
                cancellation.ThrowIfCancellationRequested();
                health.AltSweepChecks++;
                bool failed = false; Vector3 failure = start; Vector3 previous = start;
                int maximumContacts = 0;
                for (int frame = 1; frame <= frames && !failed; frame++)
                {
                    float t = MathF.Min(duration, frame);
                    float gravityTime = MathF.Max(0, t - pad.ControlLockTime);
                    Vector3 current = start + velocity * t
                        - Vector3.UnitY * (.5f * JumpPadGravity * gravityTime * gravityTime);
                    int substeps = Math.Clamp((int)MathF.Ceiling(
                        Vector3.Distance(previous, current) / PlayerSweepStep), 1, 12);
                    for (int substep = 1; substep <= substeps; substep++)
                    {
                        Vector3 sample = Vector3.Lerp(previous, current, substep / (float)substeps);
                        if (!ProbeAltForm(collision, sample, probe,
                            out int contacts, out Vector3 obstruction))
                        { failed = true; failure = obstruction; break; }
                        maximumContacts = Math.Max(maximumContacts, contacts);
                        failure = sample;
                    }
                    previous = current;
                }
                if (failed)
                {
                    health.AltSweepFailures++;
                    Record(map, new(MapCollisionRepairKind.AltFormSweepFailure, .98f,
                        $"{probe.Name} alt form intersects the authored jump-pad trajectory.",
                        pad.Target is { Length: 3 }
                            ? new[] { start, failure, V(pad.Target) } : new[] { start, failure }));
                }
                if (maximumContacts >= RuntimePlayerContactLimit)
                    Record(map, new(MapCollisionRepairKind.ContactOverflowRisk, .96f,
                        $"{probe.Name} jump-pad alt-form sweep encountered {maximumContacts} collision faces.",
                        new[] { start, failure }));
            }
        }
    }

    private static bool ProbeAllHunterAltForms(FaceIndex collision, Vector3 feet,
        out AltFormProbe? failed, out int maximumContacts, out Vector3 obstruction)
    {
        failed = null; maximumContacts = 0; obstruction = feet;
        foreach (AltFormProbe probe in _allHunterAltForms)
        {
            if (!ProbeAltForm(collision, feet, probe, out int contacts, out obstruction))
            { failed = probe; maximumContacts = Math.Max(maximumContacts, contacts); return false; }
            maximumContacts = Math.Max(maximumContacts, contacts);
        }
        return true;
    }

    private static bool ProbeAltForm(FaceIndex collision, Vector3 feet,
        AltFormProbe probe, out int contacts, out Vector3 obstruction)
    {
        contacts = 0; obstruction = feet; bool clear = true;
        Vector3 center = feet + Vector3.UnitY * probe.CenterOffset;
        foreach (BuiltFace face in collision.Query(center, probe.Radius + .25f))
        {
            if (face.IgnorePlayers || face.Points.Length < 3) continue;
            Vector3 normal = face.Normal;
            if (normal.LengthSquared < 1e-10f) continue;
            normal.Normalize();
            float distance = CapsuleFaceDistance(face, normal, center, center,
                out Vector3 closest);
            if (distance > probe.Radius + .08f) continue;
            contacts++;
            if (probe.Radius - distance <= CapsuleSkin) continue;
            // A walkable face directly beneath the grounded form is support.
            if (normal.Y >= WalkableY && closest.Y <= feet.Y + .45f) continue;
            if (clear) obstruction = closest;
            clear = false;
        }
        return clear;
    }
}
