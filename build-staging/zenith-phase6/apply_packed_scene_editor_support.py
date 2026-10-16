from __future__ import annotations
import sys
from pathlib import Path


def replace_once(path: Path, before: str, after: str, label: str) -> None:
    text = path.read_text(encoding='utf-8')
    count = text.count(before)
    if count != 1:
        raise RuntimeError(f'{label}: expected one anchor, found {count} in {path}')
    path.write_text(text.replace(before, after, 1), encoding='utf-8', newline='\n')


def main() -> int:
    if len(sys.argv) != 2:
        raise SystemExit('usage: apply_packed_scene_editor_support.py <prime-root>')
    root = Path(sys.argv[1]).resolve()

    dependencies = root / 'src/MphRead/Mods/MapGen/Build/MapDependencyAnalyzer.cs'
    replace_once(dependencies,
'''            if (definition.Collision is { } collision) FileDependency("collision", collision.Source, collision.Resolve());
            foreach (var asset in PackageAssets(definition))
''',
'''            if (definition.Collision is { } collision) FileDependency("collision", collision.Source, collision.Resolve());
            if (definition.PackedScene is { } packedScene)
                FileDependency("packed-scene", packedScene.Source, packedScene.Resolve());
            foreach (var asset in PackageAssets(definition))
''','packed scene dependency fingerprint')

    fingerprint = root / 'src/MphRead/Mods/MapGen/Build/MapBuildFingerprint.cs'
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

    print('Packed-scene dependency tracking and Studio preview integrated into', root)
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
