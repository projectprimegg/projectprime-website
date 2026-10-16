from __future__ import annotations

import re
import sys
from pathlib import Path


def main() -> int:
    if len(sys.argv) != 2:
        raise SystemExit("usage: apply_lighting_compat.py <project-prime-root>")
    root = Path(sys.argv[1]).resolve()
    path = root / "src/MphRead/Mods/RenderOptions.cs"
    text = path.read_text(encoding="utf-8")
    pattern = re.compile(
        r"        // Directional shadows are the only remaining depth-based OpenGL\n"
        r"        // presentation feature\. Native cel outlines and player outlines have\n"
        r"        // their own depth targets and are not controlled by this switch\.\n"
        r"        public static bool NeedsReadableDepth => Shadows != ShadowQuality\.Off;\n\n"
        r"        // Retired experimental post FX cannot trigger a fullscreen shader pass,\n"
        r"        // regardless of older settings or calls from diagnostic tools\.\n"
        r"        public static bool PostProcessingEnabled => Shadows != ShadowQuality\.Off;\n"
    )
    replacement = (
        "        public static bool NeedsReadableDepth => Shadows != ShadowQuality.Off;\n"
        "        public static bool PostProcessingEnabled => Shadows != ShadowQuality.Off;\n"
    )
    text, count = pattern.subn(replacement, text, count=1)
    if count != 1:
        raise RuntimeError(f"RenderOptions compatibility anchor was not found exactly once: {count}")
    path.write_text(text, encoding="utf-8", newline="\n")
    print("Phase 5 RenderOptions compatibility anchor normalized in", root)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
