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
        raise SystemExit('usage: apply_map_authoring_command.py <staging> <prime-root>')
    staging = Path(sys.argv[1]).resolve()
    root = Path(sys.argv[2]).resolve()
    mapgen = root / 'src/MphRead/Mods/MapGen'
    mods = root / 'src/MphRead/Mods'
    shutil.copy2(staging / 'MapAuthoringCommand.cs', mapgen / 'MapAuthoringCommand.cs')
    entry = mods / 'ModEntry.cs'
    anchor = '            if (HasFlag(args, "settingsarchive"))\n'
    insertion = (
        '            if (HasFlag(args, "mapcompile") || HasFlag(args, "mappackage"))\n'
        '            {\n'
        '                Environment.ExitCode = MapGen.MapAuthoringCommand.Run(args);\n'
        '                return true;\n'
        '            }\n'
        + anchor
    )
    replace_once(entry, anchor, insertion, 'headless map authoring dispatch')
    print('Headless map authoring command integrated into', root)
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
