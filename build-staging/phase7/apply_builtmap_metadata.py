from pathlib import Path
import sys

if len(sys.argv) != 2:
    raise SystemExit("usage: apply_builtmap_metadata.py <project-root>")
path = Path(sys.argv[1]).resolve() / "src/MphRead/Mods/MapGen/BuiltMap.cs"
text = path.read_text(encoding="utf-8")
before = "        public List<MapDiagnostic> ImportDiagnostics { get; } = new();\n\n        public BuiltMap(MapDefinition definition)\n"
after = "        public List<MapDiagnostic> ImportDiagnostics { get; } = new();\n        public UniversalNativeMapData? NativeFidelity { get; set; }\n\n        public BuiltMap(MapDefinition definition)\n"
if text.count(before) != 1:
    raise RuntimeError("BuiltMap metadata anchor mismatch")
path.write_text(text.replace(before, after, 1), encoding="utf-8", newline="\n")
