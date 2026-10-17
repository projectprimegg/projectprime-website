from __future__ import annotations

import sys
from pathlib import Path


def main() -> int:
    if len(sys.argv) != 2:
        raise SystemExit("usage: apply_zenith_phase7_compile_fix.py <project-prime-root>")
    root = Path(sys.argv[1]).resolve()
    path = root / "src/MphRead/Mods/MapGen/Universal/SourceArchiveUniversalAdapter.cs"
    text = path.read_text(encoding="utf-8")
    before = "normalized.StartsWith('/', StringComparison.Ordinal)"
    after = 'normalized.StartsWith("/", StringComparison.Ordinal)'
    count = text.count(before)
    if count != 1:
        raise RuntimeError(f"source-pack StartsWith compatibility: expected one anchor, found {count}")
    path.write_text(text.replace(before, after, 1), encoding="utf-8", newline="\n")
    print("Source-pack path validation compile compatibility applied in", root)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
