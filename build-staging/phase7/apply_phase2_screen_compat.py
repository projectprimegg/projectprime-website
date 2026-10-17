from __future__ import annotations

import sys
from pathlib import Path


def replace_once(text: str, before: str, after: str, label: str) -> str:
    count = text.count(before)
    if count != 1:
        raise RuntimeError(f"{label}: expected one exact anchor, found {count}")
    return text.replace(before, after, 1)


def main() -> int:
    if len(sys.argv) != 2:
        raise SystemExit("usage: apply_phase2_screen_compat.py <project-prime-root>")

    root = Path(sys.argv[1]).resolve()
    path = root / "src/MphRead/Mods/Launcher/Gui/MapStudioScreen.cs"
    text = path.read_text(encoding="utf-8")

    text = replace_once(
        text,
        '("Import 3D Model…",ImportModel),("Model Sources / Reimport…",ManageModelSources),',
        '("Import Universal 3D…",ImportModel),("Universal Importers…",ShowUniversalImporters),("Model Sources / Reimport…",ManageModelSources),',
        "Universal import menu compatibility",
    )
    text = replace_once(
        text,
        'AddButton(toolbar, "IMPORT MODEL", ImportModel);',
        'AddButton(toolbar, "IMPORT UNIVERSAL", ImportModel);',
        "Universal import toolbar compatibility",
    )

    path.write_text(text, encoding="utf-8", newline="\n")
    print("Applied deterministic Phase 2 MapStudioScreen compatibility to", path)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
