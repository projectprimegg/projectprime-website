from pathlib import Path
import sys

if len(sys.argv) != 2:
    raise SystemExit("usage: apply_builtface_uvs.py <project-root>")
path = Path(sys.argv[1]).resolve() / "src/MphRead/Mods/MapGen/BuiltMap.cs"
text = path.read_text(encoding="utf-8")
before = "        public Vector2[] Texcoords { get; }\n        public Vector3 Normal { get; }\n"
after = "        public Vector2[] Texcoords { get; }\n        /// <summary>Extra per-corner UV channels; channel zero is Texcoords.</summary>\n        public Dictionary<int, Vector2[]> AdditionalTexcoords { get; } = new();\n        public int LightmapPage { get; set; } = -1;\n        public int SourceSurfaceId { get; set; } = -1;\n        public int SourceFogId { get; set; } = -1;\n        public int VisibilityCluster { get; set; } = -1;\n        public Vector3 Normal { get; }\n"
if text.count(before) != 1:
    raise RuntimeError("BuiltFace UV anchor mismatch")
path.write_text(text.replace(before, after, 1), encoding="utf-8", newline="\n")
