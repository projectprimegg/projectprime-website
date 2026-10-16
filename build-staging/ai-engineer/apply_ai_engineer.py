from __future__ import annotations

import shutil
import sys
from pathlib import Path


def replace_once(path: Path, before: str, after: str, label: str) -> None:
    text = path.read_text(encoding="utf-8")
    count = text.count(before)
    if count != 1:
        raise RuntimeError(f"{label}: expected exactly one anchor, found {count} in {path}")
    path.write_text(text.replace(before, after, 1), encoding="utf-8", newline="\n")


def main() -> int:
    if len(sys.argv) != 3:
        raise SystemExit("usage: apply_ai_engineer.py <staging-directory> <project-prime-root>")

    staging = Path(sys.argv[1]).resolve()
    root = Path(sys.argv[2]).resolve()
    editor = root / "src/MphRead/Mods/MapEditor"
    gui = root / "src/MphRead/Mods/Launcher/Gui"
    mods = root / "src/MphRead/Mods"

    for directory in (editor, gui, mods):
        if not directory.is_dir():
            raise RuntimeError(f"Project Prime source directory is missing: {directory}")

    # Copy only the selected Phase 3 implementation. Older experiments remain in
    # staging for provenance but are intentionally not compiled.
    shutil.copy2(staging / "AiMapEngineer.cs", editor / "AiMapEngineer.cs")
    shutil.copy2(staging / "MapStudioAiEngineer.cs", gui / "MapStudioAiEngineer.cs")

    screen = gui / "MapStudioScreen.cs"
    replace_once(
        screen,
        'MenuGroup("Build",("Validate",()=>_=Validate()),("Fix selected problem",FixSelectedProblem),("Build runtime",()=>_=Build(false)),',
        'MenuGroup("Build",("AI Map Engineer",ShowAiMapEngineer),("Validate",()=>_=Validate()),("Fix selected problem",FixSelectedProblem),("Build runtime",()=>_=Build(false)),',
        "Map Studio Build menu",
    )

    toolbar_anchor = 'AddButton(toolbar, "IMPORT UNIVERSAL", ImportModel);'
    text = screen.read_text(encoding="utf-8")
    if toolbar_anchor not in text:
        # Keep the integration compatible with an upstream/non-Phase-2 source.
        toolbar_anchor = 'AddButton(toolbar, "IMPORT MODEL", ImportModel);'
    replace_once(
        screen,
        toolbar_anchor,
        toolbar_anchor + '\n            AddButton(toolbar, "AI ENGINEER", ShowAiMapEngineer);',
        "Map Studio toolbar",
    )

    entry = mods / "ModEntry.cs"
    anchor = '            if (HasFlag(args, "settingsarchive"))\n'
    insertion = (
        '            if (HasFlag(args, "aimapengineer") || HasFlag(args, "aimapengineerhelp"))\n'
        '            {\n'
        '                Environment.ExitCode = MapEditor.AiMapEngineerCommand.Run(args);\n'
        '                return true;\n'
        '            }\n'
        + anchor
    )
    replace_once(entry, anchor, insertion, "headless AI Map Engineer dispatch")

    print("AI Map Engineer Phase 3 integrated into", root)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
