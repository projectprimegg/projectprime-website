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
    shutil.copy2(staging / 'MapAltFormCertificationV2.cs',
        mapgen / 'MapAltFormCertification.cs')

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

    print('Fine-route player-form certification V2 integrated into', root)
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
