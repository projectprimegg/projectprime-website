from __future__ import annotations

import shutil
import sys
from pathlib import Path


def replace_once(path: Path, before: str, after: str, label: str) -> None:
    text = path.read_text(encoding='utf-8')
    count = text.count(before)
    if count != 1:
        raise RuntimeError(f'{label}: expected exactly one anchor, found {count} in {path}')
    path.write_text(text.replace(before, after, 1), encoding='utf-8', newline='\n')


def main() -> int:
    if len(sys.argv) != 3:
        raise SystemExit('usage: apply_zenith_phase6.py <staging-directory> <project-prime-root>')
    staging = Path(sys.argv[1]).resolve()
    root = Path(sys.argv[2]).resolve()
    mapgen = root / 'src/MphRead/Mods/MapGen'
    editor = root / 'src/MphRead/Mods/MapEditor'
    mods = root / 'src/MphRead/Mods'

    shutil.copy2(staging / 'MapPackedMeshCodec.cs', mapgen / 'Geometry/MapPackedMeshCodec.cs')
    shutil.copy2(staging / 'MapCollisionCertification.cs', mapgen / 'MapCollisionCertification.cs')
    shutil.copy2(staging / 'MapCollisionCertificationAudit.cs', mapgen / 'MapCollisionCertificationAudit.cs')
    shutil.copy2(staging / 'ZenithPhase6Acceptance.cs', editor / 'ZenithPhase6Acceptance.cs')

    geometry = mapgen / 'Geometry/MapGeometry.cs'
    replace_once(geometry,
        '    [JsonDerivedType(typeof(MapMesh), "mesh")]\n',
        '    [JsonDerivedType(typeof(MapMesh), "mesh")]\n'
        '    [JsonDerivedType(typeof(MapPackedMesh), "packedMesh")]\n',
        'packed mesh polymorphic discriminator')
    text = geometry.read_text(encoding='utf-8')
    insertion = '''\n    /// <summary>\n    /// Compressed, project-embedded triangle chunks for source maps that exceed\n    /// the editable MapMesh collection limits. The payload remains portable and\n    /// is decoded through MapPackedMeshCodec before ordinary geometry compilation.\n    /// </summary>\n    public sealed class MapPackedMesh : MapGeometry\n    {\n        public bool CollisionOnly { get; set; }\n        public int Slipperiness { get; set; }\n        public bool ReflectBeams { get; set; }\n        public bool IgnorePlayers { get; set; }\n        public bool IgnoreBeams { get; set; }\n        public bool IgnoreScan { get; set; }\n        public string Encoding { get; set; } = MapPackedMeshCodec.EncodingName;\n        public string Data { get; set; } = \"\";\n    }\n'''
    if 'public sealed class MapPackedMesh' in text:
        raise RuntimeError('packed mesh class already present')
    index = text.rfind('\n}')
    if index < 0:
        raise RuntimeError('MapGeometry namespace close not found')
    geometry.write_text(text[:index] + insertion + text[index:], encoding='utf-8', newline='\n')

    compiler = mapgen / 'Geometry/GeometryCompiler.cs'
    replace_once(compiler,
        '''        public static IReadOnlyList<BuiltFace> Compile(MapGeometry geometry, float texScale, int materialOffset = 0)\n        {\n            var vertices = new List<Vector3>();\n''',
        '''        public static IReadOnlyList<BuiltFace> Compile(MapGeometry geometry, float texScale, int materialOffset = 0)\n        {\n            if (geometry is MapPackedMesh packed)\n                return MapPackedMeshCodec.Compile(packed, texScale, materialOffset);\n            var vertices = new List<Vector3>();\n''',
        'packed geometry compile dispatch')
    replace_once(compiler,
        '''                foreach (var face in Compile(geometry, definition.Materials[geometry.Material].TexScale, materialOffset))\n                { if (geometry is not MapMesh { CollisionOnly: true }) map.Faces.Add(face); if (geometry.Solid) map.Solid.Add(face); }\n''',
        '''                foreach (var face in Compile(geometry, definition.Materials[geometry.Material].TexScale, materialOffset))\n                {\n                    bool collisionOnly = geometry is MapMesh { CollisionOnly: true }\n                        or MapPackedMesh { CollisionOnly: true };\n                    if (!collisionOnly) map.Faces.Add(face);\n                    if (geometry.Solid) map.Solid.Add(face);\n                }\n''',
        'packed collision-only compile routing')

    viewport = editor / 'MapViewportScene.cs'
    replace_once(viewport,
        '''                try { Add(g.Id,GeometryCompiler.Compile(g,g.Material>=0&&g.Material<definition.Materials.Count?definition.Materials[g.Material].TexScale:16),g.Solid,g is MapMesh { CollisionOnly: true }); }\n''',
        '''                try { Add(g.Id,GeometryCompiler.Compile(g,g.Material>=0&&g.Material<definition.Materials.Count?definition.Materials[g.Material].TexScale:16),g.Solid,\n                    g is MapMesh { CollisionOnly: true } or MapPackedMesh { CollisionOnly: true }); }\n''',
        'packed mesh viewport collision routing')

    definition = mapgen / 'MapDefinition.cs'
    replace_once(definition,
        '''        public MapCollision? Collision { get; set; }\n\n        /// <summary>Where to stand the camera for the launcher's map picture.</summary>\n''',
        '''        public MapCollision? Collision { get; set; }\n\n        /// <summary>Optional hard gate that refuses builds with unresolved final collision risks.</summary>\n        public MapCollisionCertificationSettings? CollisionCertification { get; set; }\n\n        /// <summary>Where to stand the camera for the launcher's map picture.</summary>\n''',
        'collision certification schema')

    healer = mapgen / 'MapCollisionHealer.cs'
    replace_once(healer,
        '''    ReachabilityWarning\n}\n''',
        '''    ReachabilityWarning,\n    SpawnClearanceFailure,\n    AltFormSweepFailure\n}\n''',
        'collision repair kinds')
    replace_once(healer,
        '''    public int OpenBoundaryEdges { get; set; }\n\n    public float Confidence\n''',
        '''    public int OpenBoundaryEdges { get; set; }\n    public int SpawnChecks { get; set; }\n    public int SpawnFailures { get; set; }\n    public int AltSweepChecks { get; set; }\n    public int AltSweepFailures { get; set; }\n\n    public float Confidence\n''',
        'collision certification counters')
    replace_once(healer,
        '''            if (ProbeCount + SweepCount == 0) return 1;\n            int total = ProbeCount + SweepCount;\n            int failures = ProbeFailures + SweepFailures;\n''',
        '''            if (ProbeCount + SweepCount + SpawnChecks + AltSweepChecks == 0) return 1;\n            int total = ProbeCount + SweepCount + SpawnChecks + AltSweepChecks;\n            int failures = ProbeFailures + SweepFailures + SpawnFailures + AltSweepFailures;\n''',
        'collision confidence certification counters')
    replace_once(healer,
        'public static class MapCollisionHealer\n',
        'public static partial class MapCollisionHealer\n',
        'partial collision healer')

    validator = mapgen / 'Validation/MapValidator.cs'
    replace_once(validator,
        '''            if(d.Partitioning is {} partition)\n            {\n''',
        '''            if (d.CollisionCertification is { Enabled: true } certification\n                && !certification.RequireAllHunterAltForms\n                && !certification.RequireZeroProbeFailures\n                && !certification.RequireZeroSweepFailures\n                && !certification.RequireZeroContactOverflow\n                && !certification.RequireSafeSpawns\n                && !certification.RequireCleanTopology\n                && !certification.RequireConnectedTraversal)\n                r.Error("FP-COLLISION-001", "Enabled collision certification must select at least one gate.");\n            if(d.Partitioning is {} partition)\n            {\n''',
        'collision certification source validation')
    replace_once(validator,
        '''                    var faces=GeometryCompiler.Compile(geometry, d.Materials[geometry.Material].TexScale);if(geometry.Solid)solids.Add(faces);\n                    float bound=8*MathF.Pow(2,Math.Clamp(d.ScaleFactor,0,16));\n''',
        '''                    var faces=GeometryCompiler.Compile(geometry, d.Materials[geometry.Material].TexScale);if(geometry.Solid)solids.Add(faces);\n                    if(faces.Any(face=>face.Material<0||face.Material>=d.Materials.Count))\n                        r.Error("FP-MAP-001","Compiled geometry face references a missing material.",geometry.Id);\n                    float bound=8*MathF.Pow(2,Math.Clamp(d.ScaleFactor,0,16));\n''',
        'packed face material validation')

    map_compiler = mapgen / 'Build/MapCompiler.cs'
    replace_once(map_compiler,
        '''                MapPacker.ApplyCollision(map, snapshot, verbose: false);\n                map.SourceDefinition = definition;\n                cancellation.ThrowIfCancellationRequested();\n                MapBudgetValidator.Analyze(map, result);\n''',
        '''                MapPacker.ApplyCollision(map, snapshot, verbose: false);\n                if (snapshot.CollisionCertification?.Enabled == true)\n                    MapCollisionHealer.Certify(map, snapshot, cancellation);\n                map.SourceDefinition = definition;\n                cancellation.ThrowIfCancellationRequested();\n                MapBudgetValidator.Analyze(map, result);\n                MapCollisionCertification.Validate(snapshot, map, result);\n''',
        'final collision certification build gate')

    budgets = mapgen / 'Validation/MapBudgetValidator.cs'
    replace_once(budgets,
        '''                Add(result,"Collision probes",health.ProbeCount+health.SweepCount);\n                Add(result,"Collision probe failures",health.ProbeFailures+health.SweepFailures);\n''',
        '''                Add(result,"Collision probes",health.ProbeCount+health.SweepCount);\n                Add(result,"Collision probe failures",health.ProbeFailures+health.SweepFailures);\n                Add(result,"Certified spawn checks",health.SpawnChecks);\n                Add(result,"All-hunter alt-form checks",health.AltSweepChecks);\n                Add(result,"Alt-form clearance failures",health.AltSweepFailures);\n''',
        'collision certification budgets')

    entry = mods / 'ModEntry.cs'
    anchor = '            if (HasFlag(args, "studioexperiencecheck"))\n'
    insertion = (
        '            if (HasFlag(args, "zenithphase6check"))\n'
        '            {\n'
        '                Environment.ExitCode = MapEditor.ZenithPhase6CheckCommand.Run(args);\n'
        '                return true;\n'
        '            }\n'
        + anchor
    )
    replace_once(entry, anchor, insertion, 'Zenith Phase 6 acceptance dispatch')

    print('Zenith Phase 6 packed geometry and collision certification integrated into', root)
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
