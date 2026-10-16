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
        raise SystemExit('usage: apply_packed_scene_package_reader.py <prime-root>')
    root = Path(sys.argv[1]).resolve()
    reader = root / 'src/MphRead/Mods/MapGen/Packaging/MapPackageReader.cs'
    replace_once(reader,
'''                if (ext is not (".json" or ".bsp" or ".obj" or ".tex" or ".png"
                    or ".jpg" or ".jpeg" or ".tga" or ".ktx2" or ".ogg" or ".wav" or ".mp3"))
''',
'''                if (ext is not (".json" or ".bsp" or ".obj" or ".ppmesh" or ".tex" or ".png"
                    or ".jpg" or ".jpeg" or ".tga" or ".ktx2" or ".ogg" or ".wav" or ".mp3"))
''','packed scene package extension')
    replace_once(reader,
'''            if (definition.Import is { } import)
            {
''',
'''            if (definition.PackedScene is { } packedScene)
            {
                if (String.IsNullOrWhiteSpace(packedScene.Source)
                    || !packedScene.Source.EndsWith(".ppmesh", StringComparison.OrdinalIgnoreCase)
                    || !entries.ContainsKey(CanonicalName(packedScene.Source)))
                    throw new InvalidDataException("Packaged large-scene asset is missing or invalid.");
            }
            if (definition.Import is { } import)
            {
''','packed scene package reference validation')
    print('Packed scene package-reader support integrated into', root)
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
