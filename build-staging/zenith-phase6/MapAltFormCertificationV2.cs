using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenTK.Mathematics;

namespace MphRead.Mods.MapGen;

public sealed record MapPlayerFormProfile(
    string Name,
    bool Biped,
    float Radius,
    float Height,
    float CenterYOffset,
    float MaximumSpeed);

public sealed record MapPlayerFormFailure(
    string Form,
    string Kind,
    Vector3 Start,
    Vector3 Failure,
    Vector3 End,
    int Contacts,
    string Detail);

public sealed record MapAltFormCertificationReport(
    bool Passed,
    int Profiles,
    int Segments,
    int Samples,
    int MaximumContacts,
    IReadOnlyList<MapPlayerFormFailure> Failures)
{
    public string Summary => Passed
        ? $"All {Profiles} biped/alt-form profiles passed {Segments:N0} spawn-reachable fine route segments and {Samples:N0} swept samples."
        : $"{Failures.Count:N0} biped/alt-form collision failure(s) were found across {Segments:N0} spawn-reachable fine route segments.";
}

/// <summary>
/// Mandatory collision gate for biped movement and every MPH hunter alt form.
/// Certification follows the pre-decimation, short-step collision graph only in
/// components reached by player spawns or authored traversal endpoints. It does
/// not draw false straight lines between coarse bot-navigation representatives.
/// Jump pads are checked separately along their actual parabolic flight path.
/// </summary>
public static class MapAltFormCertification
{
    private const float WalkableY = .35f;
    private const float BipedRadius = .5f;
    private const float BipedHeight = 1.6f;
    private const float Skin = .025f;
    private const int ContactLimit = 40;
    private const int MaximumFailures = 512;
    private const float WalkingSweepStep = .125f;
    private const float AirSweepStep = .125f;
    private const float JumpPadGravity = 77 / 4096f;

    public static IReadOnlyList<MapPlayerFormProfile> Profiles { get; } = CreateProfiles();

    private static IReadOnlyList<MapPlayerFormProfile> CreateProfiles()
    {
        var result = new List<MapPlayerFormProfile>
        {
            new("Biped", true, BipedRadius, BipedHeight, BipedRadius, 0)
        };
        int hunters = Math.Min(7, Metadata.PlayerValues.Count);
        for (int i = 0; i < hunters; i++)
        {
            var values = Metadata.PlayerValues[i];
            float radius = Fixed.ToFloat(values.AltColRadius);
            float y = Fixed.ToFloat(values.AltColYPos);
            float maximum = Math.Max(Fixed.ToFloat(values.BoostSpeedMax),
                Fixed.ToFloat(values.BoostSpeedMin));
            result.Add(new(((Hunter)i) + " alt form", false,
                radius, radius * 2, y, maximum));
        }
        return result;
    }

    public static MapAltFormCertificationReport Certify(BuiltMap map,
        MapDefinition definition, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(definition);
        BuiltFace[] faces = map.Solid.SelectMany(MapPacker.CollisionParts)
            .Where(face => !face.IgnorePlayers && face.Points.Length >= 3).ToArray();
        if (faces.Length == 0)
            return new(false, Profiles.Count, 0, 0, 0,
                new[] { new MapPlayerFormFailure("All", "NoCollision", Vector3.Zero,
                    Vector3.Zero, Vector3.Zero, 0,
                    "The compiled map contains no player collision.") });

        MapNodePacker.NavigationGraph graph =
            MapNodePacker.AnalyzeFineForCertification(map.Solid, cancellation);
        HashSet<int> includedComponents = IncludedComponents(graph, definition);
        var collision = new FaceIndex(faces, 3f);
        var failures = new List<MapPlayerFormFailure>();
        int segments = 0, samples = 0, maximumContacts = 0;

        void AddFailure(MapPlayerFormProfile profile, string kind,
            Vector3 start, Vector3 failure, Vector3 end, int contacts, string detail)
        {
            if (failures.Count >= MaximumFailures) return;
            failures.Add(new(profile.Name, kind, start, failure, end, contacts, detail));
        }

        void TestGroundedSegment(Vector3 start, Vector3 end, string route)
        {
            if (failures.Count >= MaximumFailures) return;
            segments++;
            float distance = Vector3.Distance(start, end);
            int steps = Math.Clamp((int)MathF.Ceiling(distance / WalkingSweepStep), 1, 128);
            bool[] active = Enumerable.Repeat(true, Profiles.Count).ToArray();
            for (int step = 0; step <= steps && active.Any(value => value); step++)
            {
                cancellation.ThrowIfCancellationRequested();
                Vector3 sample = Vector3.Lerp(start, end, step / (float)steps);
                if (!TryFloor(collision, sample + Vector3.UnitY * 1.25f,
                    3f, 1.35f, out float floor))
                {
                    for (int p = 0; p < Profiles.Count; p++)
                    {
                        if (!active[p]) continue;
                        samples++;
                        AddFailure(Profiles[p], "FloorGap", start, sample, end, 0,
                            route + " has no usable floor below the swept body.");
                        active[p] = false;
                    }
                    break;
                }

                Vector3 feet = new(sample.X, floor + Skin, sample.Z);
                BuiltFace[] candidates = collision.Query(feet + Vector3.UnitY * .8f, 1.75f).ToArray();
                for (int p = 0; p < Profiles.Count; p++)
                {
                    if (!active[p]) continue;
                    samples++;
                    MapPlayerFormProfile profile = Profiles[p];
                    bool clear = ProbeProfile(candidates, feet, profile,
                        out int contacts, out Vector3 obstruction);
                    maximumContacts = Math.Max(maximumContacts, contacts);
                    if (contacts >= ContactLimit)
                    {
                        AddFailure(profile, "ContactOverflow", start, obstruction, end,
                            contacts, route + $" reaches {contacts} simultaneous collision contacts; runtime capacity is {ContactLimit}.");
                        active[p] = false;
                    }
                    else if (!clear)
                    {
                        AddFailure(profile, "BodyObstruction", start, obstruction, end,
                            contacts, route + " intersects wall, ramp edge, ceiling or side geometry.");
                        active[p] = false;
                    }
                }
            }
        }

        void TestAirSegment(Vector3 start, Vector3 end, string route)
        {
            if (failures.Count >= MaximumFailures) return;
            segments++;
            float distance = Vector3.Distance(start, end);
            int steps = Math.Clamp((int)MathF.Ceiling(distance / AirSweepStep), 1, 512);
            bool[] active = Enumerable.Repeat(true, Profiles.Count).ToArray();
            for (int step = 0; step <= steps && active.Any(value => value); step++)
            {
                cancellation.ThrowIfCancellationRequested();
                Vector3 feet = Vector3.Lerp(start, end, step / (float)steps);
                BuiltFace[] candidates = collision.Query(feet + Vector3.UnitY * .8f, 1.75f).ToArray();
                for (int p = 0; p < Profiles.Count; p++)
                {
                    if (!active[p]) continue;
                    samples++;
                    MapPlayerFormProfile profile = Profiles[p];
                    bool clear = ProbeProfile(candidates, feet, profile,
                        out int contacts, out Vector3 obstruction);
                    maximumContacts = Math.Max(maximumContacts, contacts);
                    if (contacts >= ContactLimit || !clear)
                    {
                        AddFailure(profile,
                            contacts >= ContactLimit ? "ContactOverflow" : "AirObstruction",
                            start, obstruction, end, contacts,
                            route + (contacts >= ContactLimit
                                ? $" reaches {contacts} simultaneous collision contacts; runtime capacity is {ContactLimit}."
                                : " intersects collision during airborne traversal."));
                        active[p] = false;
                    }
                }
            }
        }

        var edges = new HashSet<(int A, int B)>();
        for (int a = 0; a < graph.Neighbours.Length
            && failures.Count < MaximumFailures; a++)
        {
            if (!includedComponents.Contains(graph.Components[a])) continue;
            foreach (int b in graph.Neighbours[a])
            {
                if (!includedComponents.Contains(graph.Components[b])) continue;
                int low = Math.Min(a, b), high = Math.Max(a, b);
                if (low == high || !edges.Add((low, high))) continue;
                TestGroundedSegment(graph.Positions[low], graph.Positions[high],
                    $"Fine traversal edge {low}->{high}");
                if (failures.Count >= MaximumFailures) break;
            }
        }

        // Every spawn receives radial short-route checks. This catches a spawn
        // placed against trim or inside concave collision even when its graph
        // component itself is otherwise valid.
        foreach (MapSpawn spawn in definition.Spawns)
        {
            Vector3 origin = V(spawn.Position);
            TestGroundedSegment(origin, origin, "Spawn standing clearance");
            for (int direction = 0; direction < 16
                && failures.Count < MaximumFailures; direction++)
            {
                float angle = direction * MathF.Tau / 16;
                Vector3 end = origin + new Vector3(MathF.Cos(angle), 0,
                    MathF.Sin(angle)) * 3f;
                TestGroundedSegment(origin, end, "Spawn radial clearance");
            }
        }

        foreach (MapJumpPad pad in definition.JumpPads)
        {
            if (failures.Count >= MaximumFailures) break;
            TestJumpPad(pad, collision, TestAirSegment, TestGroundedSegment,
                ref segments, ref samples, ref maximumContacts,
                failures, AddFailure, cancellation);
        }

        // Authoring links that are not represented by a jump-pad entity still
        // receive endpoint and airborne-path checks. Teleporters intentionally
        // check only their two safe endpoints because no physical path exists.
        foreach (MapNavigationLink link in definition.NavigationLinks)
        {
            if (failures.Count >= MaximumFailures) break;
            if (link.Kind == MapNavigationLinkKind.JumpPad
                && definition.JumpPads.Any(pad => SamePoint(pad.Position, link.From)
                    && pad.Target != null && SamePoint(pad.Target, link.To)))
                continue;
            Vector3 from = V(link.From), to = V(link.To);
            TestGroundedSegment(from, from, $"{link.Kind} start clearance");
            TestGroundedSegment(to, to, $"{link.Kind} destination clearance");
            if (link.Kind != MapNavigationLinkKind.Teleporter)
                TestAirSegment(from, to, $"{link.Kind} authored traversal");
        }

        return new(failures.Count == 0, Profiles.Count, segments, samples,
            maximumContacts, failures.ToArray());
    }

    private static HashSet<int> IncludedComponents(
        MapNodePacker.NavigationGraph graph, MapDefinition definition)
    {
        var result = new HashSet<int>();
        int Nearest(float[] point, out float distanceSquared)
        {
            Vector3 value = V(point);
            int best = -1;
            distanceSquared = float.MaxValue;
            for (int i = 0; i < graph.Positions.Length; i++)
            {
                float candidate = Vector3.DistanceSquared(value, graph.Positions[i]);
                if (candidate < distanceSquared)
                {
                    best = i;
                    distanceSquared = candidate;
                }
            }
            return best;
        }
        void Include(float[] point)
        {
            int node = Nearest(point, out float distanceSquared);
            if (node >= 0 && distanceSquared <= 144f)
                result.Add(graph.Components[node]);
        }

        foreach (MapSpawn spawn in definition.Spawns) Include(spawn.Position);
        foreach (MapJumpPad pad in definition.JumpPads)
        {
            Include(pad.Position);
            if (pad.Target is { Length: 3 }) Include(pad.Target);
        }
        foreach (MapNavigationLink link in definition.NavigationLinks)
        {
            Include(link.From);
            Include(link.To);
        }
        if (result.Count == 0 && graph.Components.Length > 0)
        {
            int largest = graph.Components.GroupBy(value => value)
                .OrderByDescending(group => group.Count()).First().Key;
            result.Add(largest);
        }
        return result;
    }

    private static void TestJumpPad(MapJumpPad pad, FaceIndex collision,
        Action<Vector3, Vector3, string> testAir,
        Action<Vector3, Vector3, string> testGround,
        ref int segments, ref int samples, ref int maximumContacts,
        List<MapPlayerFormFailure> failures,
        Action<MapPlayerFormProfile, string, Vector3, Vector3, Vector3, int, string> addFailure,
        CancellationToken cancellation)
    {
        Vector3 start = V(pad.Position);
        Vector3 direction;
        float speed;
        try { (direction, speed) = MapBuilder.SolveJumpPad(pad); }
        catch (Exception ex) when (ex is ProgramException or ArgumentException)
        {
            foreach (MapPlayerFormProfile profile in Profiles)
                addFailure(profile, "JumpPadUnsolved", start, start, start, 0,
                    $"Jump pad '{pad.Label ?? pad.Id.ToString()}' cannot be solved: {ex.Message}");
            return;
        }

        Vector3 velocity = direction * speed;
        float duration = JumpPadDuration(pad, start, velocity);
        Vector3 previous = start;
        int frames = Math.Clamp((int)MathF.Ceiling(duration), 1, 240);
        for (int frame = 1; frame <= frames && failures.Count < MaximumFailures; frame++)
        {
            cancellation.ThrowIfCancellationRequested();
            float t = MathF.Min(duration, frame);
            float gravityTime = MathF.Max(0, t - pad.ControlLockTime);
            Vector3 current = start + velocity * t
                - Vector3.UnitY * (.5f * JumpPadGravity * gravityTime * gravityTime);
            testAir(previous, current, $"Jump pad '{pad.Label ?? pad.Id.ToString()}' trajectory");
            previous = current;
        }
        if (pad.Target is { Length: 3 })
        {
            Vector3 target = V(pad.Target);
            testGround(target, target, $"Jump pad '{pad.Label ?? pad.Id.ToString()}' landing clearance");
        }
    }

    private static float JumpPadDuration(MapJumpPad pad, Vector3 start, Vector3 velocity)
    {
        if (pad.Target is { Length: 3 })
        {
            Vector3 delta = V(pad.Target) - start;
            float horizontalDistance = new Vector3(delta.X, 0, delta.Z).Length;
            float horizontalSpeed = new Vector3(velocity.X, 0, velocity.Z).Length;
            if (horizontalSpeed > 1e-5f)
                return Math.Clamp(horizontalDistance / horizontalSpeed, 1, 240);
            float discriminant = velocity.Y * velocity.Y - 2 * JumpPadGravity * delta.Y;
            if (discriminant >= 0)
            {
                float root = (velocity.Y + MathF.Sqrt(discriminant)) / JumpPadGravity;
                if (root > 0) return Math.Clamp(root, 1, 240);
            }
        }
        return Math.Clamp(Math.Max(30, (int)pad.ControlLockTime), 1, 180);
    }

    private static bool ProbeProfile(IReadOnlyList<BuiltFace> candidates,
        Vector3 feet, MapPlayerFormProfile profile,
        out int contacts, out Vector3 obstruction)
    {
        if (profile.Biped)
            return ProbeCapsule(candidates, feet + Vector3.UnitY * profile.Radius,
                feet + Vector3.UnitY * (profile.Height - profile.Radius),
                feet, profile.Radius, out contacts, out obstruction);

        Vector3 centerA = feet + Vector3.UnitY * profile.Radius;
        Vector3 centerB = feet + Vector3.UnitY * Math.Max(profile.Radius,
            profile.CenterYOffset);
        bool clearA = ProbeCapsule(candidates, centerA, centerA, feet,
            profile.Radius, out int contactsA, out Vector3 obstructionA);
        if (Vector3.DistanceSquared(centerA, centerB) <= 1e-8f)
        {
            contacts = contactsA;
            obstruction = obstructionA;
            return clearA;
        }
        bool clearB = ProbeCapsule(candidates, centerB, centerB, feet,
            profile.Radius, out int contactsB, out Vector3 obstructionB);
        contacts = Math.Max(contactsA, contactsB);
        obstruction = !clearA ? obstructionA : obstructionB;
        return clearA && clearB;
    }

    private static bool ProbeCapsule(IReadOnlyList<BuiltFace> candidates,
        Vector3 bottom, Vector3 top, Vector3 feet, float radius,
        out int contacts, out Vector3 obstruction)
    {
        contacts = 0;
        obstruction = (bottom + top) * .5f;
        bool clear = true;
        foreach (BuiltFace face in candidates)
        {
            if (face.IgnorePlayers || face.Points.Length < 3) continue;
            Vector3 normal = face.Normal;
            if (normal.LengthSquared < 1e-10f) continue;
            normal.Normalize();
            float distance = CapsuleFaceDistance(face, normal, bottom, top,
                out Vector3 closest);
            if (distance > radius + .08f) continue;
            contacts++;
            if (radius - distance <= Skin) continue;
            if (normal.Y >= WalkableY && closest.Y <= feet.Y + .45f) continue;
            if (clear) obstruction = closest;
            clear = false;
        }
        return clear;
    }

    private static bool TryFloor(FaceIndex collision, Vector3 origin,
        float down, float up, out float floor)
    {
        floor = float.MinValue;
        foreach (BuiltFace face in collision.Query(origin, Math.Max(down, up) + 1f))
        {
            Vector3 normal = face.Normal;
            if (normal.LengthSquared < 1e-10f) continue;
            normal.Normalize();
            if (normal.Y < WalkableY) continue;
            float t = Vector3.Dot(normal, face.Points[0] - origin) / normal.Y;
            if (t < -down || t > up) continue;
            Vector3 hit = origin + Vector3.UnitY * t;
            if (!Accepts(face, hit)) continue;
            floor = Math.Max(floor, hit.Y);
        }
        return floor != float.MinValue;
    }

    private static float CapsuleFaceDistance(BuiltFace face, Vector3 normal,
        Vector3 bottom, Vector3 top, out Vector3 closest)
    {
        float bottomDistance = Vector3.Dot(normal, bottom - face.Points[0]);
        float topDistance = Vector3.Dot(normal, top - face.Points[0]);
        float t;
        if (bottomDistance * topDistance <= 0
            && MathF.Abs(bottomDistance - topDistance) > 1e-6f)
            t = Math.Clamp(bottomDistance / (bottomDistance - topDistance), 0, 1);
        else
            t = MathF.Abs(bottomDistance) <= MathF.Abs(topDistance) ? 0 : 1;
        Vector3 centre = Vector3.Lerp(bottom, top, t);
        float signedDistance = Vector3.Dot(normal, centre - face.Points[0]);
        Vector3 projected = centre - normal * signedDistance;
        if (Accepts(face, projected))
        {
            closest = projected;
            return MathF.Abs(signedDistance);
        }
        float bestSquared = float.MaxValue;
        closest = projected;
        for (int i = 0; i < face.Points.Length; i++)
        {
            float squared = SegmentSegmentDistanceSquared(bottom, top,
                face.Points[i], face.Points[(i + 1) % face.Points.Length],
                out _, out Vector3 edgePoint);
            if (squared < bestSquared)
            {
                bestSquared = squared;
                closest = edgePoint;
            }
        }
        return bestSquared == float.MaxValue
            ? float.MaxValue : MathF.Sqrt(bestSquared);
    }

    private static float SegmentSegmentDistanceSquared(Vector3 p1, Vector3 q1,
        Vector3 p2, Vector3 q2, out Vector3 c1, out Vector3 c2)
    {
        const float epsilon = 1e-8f;
        Vector3 d1 = q1 - p1, d2 = q2 - p2, r = p1 - p2;
        float a = Vector3.Dot(d1, d1), e = Vector3.Dot(d2, d2);
        float f = Vector3.Dot(d2, r), s, t;
        if (a <= epsilon && e <= epsilon)
        {
            c1 = p1; c2 = p2;
            return Vector3.DistanceSquared(c1, c2);
        }
        if (a <= epsilon)
        {
            s = 0; t = Math.Clamp(f / e, 0, 1);
        }
        else
        {
            float c = Vector3.Dot(d1, r);
            if (e <= epsilon)
            {
                t = 0; s = Math.Clamp(-c / a, 0, 1);
            }
            else
            {
                float b = Vector3.Dot(d1, d2);
                float denominator = a * e - b * b;
                s = MathF.Abs(denominator) > epsilon
                    ? Math.Clamp((b * f - c * e) / denominator, 0, 1) : 0;
                float numerator = b * s + f;
                if (numerator < 0)
                {
                    t = 0; s = Math.Clamp(-c / a, 0, 1);
                }
                else if (numerator > e)
                {
                    t = 1; s = Math.Clamp((b - c) / a, 0, 1);
                }
                else t = numerator / e;
            }
        }
        c1 = p1 + d1 * s;
        c2 = p2 + d2 * t;
        return Vector3.DistanceSquared(c1, c2);
    }

    private static bool Accepts(BuiltFace face, Vector3 point)
    {
        Vector3 normal = face.Normal;
        for (int i = 0; i < face.Points.Length; i++)
        {
            Vector3 a = face.Points[i], b = face.Points[(i + 1) % face.Points.Length];
            if (Vector3.Dot(Vector3.Cross(b - a, point - a), normal) < -.002f)
                return false;
        }
        return true;
    }

    private static bool SamePoint(float[] a, float[] b)
        => a.Length == 3 && b.Length == 3
            && MathF.Abs(a[0] - b[0]) <= .25f
            && MathF.Abs(a[1] - b[1]) <= .25f
            && MathF.Abs(a[2] - b[2]) <= .25f;

    private static Vector3 V(float[] value) => new(value[0], value[1], value[2]);

    private sealed class FaceIndex
    {
        private readonly Dictionary<(int X, int Y, int Z), List<BuiltFace>> _cells = new();
        private readonly List<BuiltFace> _large = new();
        private readonly float _cell;

        public FaceIndex(IEnumerable<BuiltFace> faces, float cell)
        {
            _cell = cell;
            foreach (BuiltFace face in faces)
            {
                Vector3 min = face.Points.Aggregate(new Vector3(float.MaxValue),
                    Vector3.ComponentMin);
                Vector3 max = face.Points.Aggregate(new Vector3(float.MinValue),
                    Vector3.ComponentMax);
                (int X, int Y, int Z) low = Cell(min), high = Cell(max);
                long count = (long)(high.X - low.X + 1)
                    * (high.Y - low.Y + 1) * (high.Z - low.Z + 1);
                if (count > 256)
                {
                    _large.Add(face);
                    continue;
                }
                for (int x = low.X; x <= high.X; x++)
                    for (int y = low.Y; y <= high.Y; y++)
                        for (int z = low.Z; z <= high.Z; z++)
                        {
                            var key = (x, y, z);
                            if (!_cells.TryGetValue(key, out List<BuiltFace>? list))
                                _cells[key] = list = new();
                            list.Add(face);
                        }
            }
        }

        public IEnumerable<BuiltFace> Query(Vector3 center, float radius)
        {
            (int X, int Y, int Z) low = Cell(center - new Vector3(radius));
            (int X, int Y, int Z) high = Cell(center + new Vector3(radius));
            var seen = new HashSet<BuiltFace>();
            foreach (BuiltFace face in _large)
                if (seen.Add(face)) yield return face;
            for (int x = low.X; x <= high.X; x++)
                for (int y = low.Y; y <= high.Y; y++)
                    for (int z = low.Z; z <= high.Z; z++)
                        if (_cells.TryGetValue((x, y, z), out List<BuiltFace>? list))
                            foreach (BuiltFace face in list)
                                if (seen.Add(face)) yield return face;
        }

        private (int X, int Y, int Z) Cell(Vector3 value)
            => ((int)MathF.Floor(value.X / _cell),
                (int)MathF.Floor(value.Y / _cell),
                (int)MathF.Floor(value.Z / _cell));
    }
}
