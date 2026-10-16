from __future__ import annotations
import shutil, sys
from pathlib import Path


def replace_once(path: Path, before: str, after: str, label: str) -> None:
    text = path.read_text(encoding='utf-8')
    count = text.count(before)
    if count != 1:
        raise RuntimeError(f'{label}: expected one anchor, found {count} in {path}')
    path.write_text(text.replace(before, after, 1), encoding='utf-8', newline='\n')


def main() -> int:
    if len(sys.argv) != 3:
        raise SystemExit('usage: apply_certification_v2.py <staging> <prime-root>')
    staging = Path(sys.argv[1]).resolve()
    root = Path(sys.argv[2]).resolve()
    mapgen = root / 'src/MphRead/Mods/MapGen'
    certification = mapgen / 'MapAltFormCertification.cs'
    shutil.copy2(staging / 'MapAltFormCertificationV2.cs', certification)

    # A larger alt form is allowed to be intentionally blocked from a narrow
    # biped route. It becomes a certification failure only when that form fits
    # at both endpoints but collides or loses support between them. Spawns,
    # launch paths, destinations and authored traversal endpoints still require
    # every profile because players may enter those locations in any form.
    replace_once(certification,
'''        void TestGroundedSegment(Vector3 start, Vector3 end, string route)
''',
'''        void TestGroundedSegment(Vector3 start, Vector3 end, string route,
            bool requireAllProfiles = false)
''','route eligibility signature')
    replace_once(certification,
'''            bool[] active = Enumerable.Repeat(true, Profiles.Count).ToArray();
            for (int step = 0; step <= steps && active.Any(value => value); step++)
''',
'''            bool[] active = Enumerable.Repeat(true, Profiles.Count).ToArray();
            if (!requireAllProfiles
                && TryFloor(collision, start + Vector3.UnitY * 1.25f, 3f, 1.35f, out float startFloor)
                && TryFloor(collision, end + Vector3.UnitY * 1.25f, 3f, 1.35f, out float endFloor))
            {
                Vector3 startFeet = new(start.X, startFloor + Skin, start.Z);
                Vector3 endFeet = new(end.X, endFloor + Skin, end.Z);
                BuiltFace[] startCandidates = collision.Query(startFeet + Vector3.UnitY * .8f, 1.75f).ToArray();
                BuiltFace[] endCandidates = collision.Query(endFeet + Vector3.UnitY * .8f, 1.75f).ToArray();
                // Biped remains mandatory because this graph is itself built from
                // biped traversal. Other profiles are tested only where both ends
                // are valid for that profile, avoiding false errors for deliberate
                // narrow passages while still catching mid-route snags and tunneling.
                for (int p = 1; p < Profiles.Count; p++)
                {
                    bool startClear = ProbeProfile(startCandidates, startFeet, Profiles[p],
                        out int startContacts, out _);
                    bool endClear = ProbeProfile(endCandidates, endFeet, Profiles[p],
                        out int endContacts, out _);
                    active[p] = startClear && endClear
                        && startContacts < ContactLimit && endContacts < ContactLimit;
                }
            }
            for (int step = 0; step <= steps && active.Any(value => value); step++)
''','profile-specific route eligibility')
    replace_once(certification,
'''            TestGroundedSegment(origin, origin, "Spawn standing clearance");
''',
'''            TestGroundedSegment(origin, origin, "Spawn standing clearance", requireAllProfiles: true);
''','all-profile spawn standing check')
    replace_once(certification,
'''                TestGroundedSegment(origin, end, "Spawn radial clearance");
''',
'''                TestGroundedSegment(origin, end, "Spawn radial clearance", requireAllProfiles: true);
''','all-profile spawn radial check')
    replace_once(certification,
'''            TestJumpPad(pad, collision, TestAirSegment, TestGroundedSegment,
''',
'''            TestJumpPad(pad, collision, TestAirSegment,
                (start, end, route) => TestGroundedSegment(start, end, route, requireAllProfiles: true),
''','all-profile jump-pad landing check')
    replace_once(certification,
'''            TestGroundedSegment(from, from, $"{link.Kind} start clearance");
            TestGroundedSegment(to, to, $"{link.Kind} destination clearance");
''',
'''            TestGroundedSegment(from, from, $"{link.Kind} start clearance", requireAllProfiles: true);
            TestGroundedSegment(to, to, $"{link.Kind} destination clearance", requireAllProfiles: true);
''','all-profile authored endpoint checks')

    healer = mapgen / 'MapCollisionHealer.cs'
    replace_once(healer,
'''        try { graph = MapNodePacker.Analyze(map.Solid, definition.NavigationLinks, cancellation); }
''',
'''        try { graph = MapNodePacker.AnalyzeFineForCertification(map.Solid, cancellation); }
''','fine graph collision-healer audit')
    replace_once(healer,
'''                    if (b <= a) continue;
                    cancellation.ThrowIfCancellationRequested();
''',
'''                    if (b <= a) continue;
                    if (!spawnComponents.Contains(graph.Components[a])
                        || !spawnComponents.Contains(graph.Components[b])) continue;
                    cancellation.ThrowIfCancellationRequested();
''','spawn-reachable healer edge filter')

    validator = mapgen / 'Validation/MapValidator.cs'
    replace_once(validator,
'''                    if (d.Import == null && d.Geometry.Count == 0 && !d.Brushes.Any(b => b.Solid && Supported(s.Position, b)))
                        r.Warning("FP-MAP-014", "Spawn has no nearby supporting floor.", s.Id);
''',
'''                    if (d.Import == null && d.PackedScene == null && d.Geometry.Count == 0
                        && !d.Brushes.Any(b => b.Solid && Supported(s.Position, b)))
                        r.Warning("FP-MAP-014", "Spawn has no nearby supporting floor.", s.Id);
''','packed scene spawn source warning')

    dependencies = mapgen / 'Build/MapDependencyAnalyzer.cs'
    replace_once(dependencies,
'''            if (definition.Collision is { } collision) FileDependency("collision", collision.Source, collision.Resolve());
            foreach (var asset in PackageAssets(definition))
''',
'''            if (definition.Collision is { } collision) FileDependency("collision", collision.Source, collision.Resolve());
            if (definition.PackedScene is { } packedScene)
                FileDependency("packed-scene", packedScene.Source, packedScene.Resolve());
            foreach (var asset in PackageAssets(definition))
''','packed scene dependency fingerprint')

    fingerprint = mapgen / 'Build/MapBuildFingerprint.cs'
    replace_once(fingerprint,
'''        public const int CurrentCompilerVersion = 10;
''',
'''        public const int CurrentCompilerVersion = 11;
''','Phase 6 compiler fingerprint')

    screen = root / 'src/MphRead/Mods/Launcher/Gui/MapStudioScreen.cs'
    replace_once(screen,
'''            if(p.Definition.Import==null&&p.Definition.NativeRoom==null)return;
''',
'''            if(p.Definition.Import==null&&p.Definition.NativeRoom==null&&p.Definition.PackedScene==null)return;
''','packed scene preview eligibility')
    replace_once(screen,
'''                string kind=p.Definition.NativeRoom!=null?"Native room":"Imported map";
''',
'''                string kind=p.Definition.NativeRoom!=null?"Native room"
                    :p.Definition.PackedScene!=null?"Packed large scene":"Imported map";
''','packed scene preview status')
    replace_once(screen,
'''            if((p.Definition.Import!=null||p.Definition.NativeRoom!=null)&&result.Faces.Length>0)foreach(var view in _views)view.SetImported(result);
''',
'''            if((p.Definition.Import!=null||p.Definition.NativeRoom!=null||p.Definition.PackedScene!=null)
                &&result.Faces.Length>0)foreach(var view in _views)view.SetImported(result);
''','packed scene validation preview')

    print('Profile-aware fine-route certification and packed-scene Studio support integrated into', root)
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
