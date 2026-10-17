from pathlib import Path
import sys

if len(sys.argv) != 2:
    raise SystemExit("usage: apply_mapmesh_uvs.py <project-root>")
path = Path(sys.argv[1]).resolve() / "src/MphRead/Mods/MapGen/Geometry/MapGeometry.cs"
text = path.read_text(encoding="utf-8")
before = "        public List<float[][]?> FaceTexcoords { get; set; } = new();\n    }\n}\n"
after = "        public List<float[][]?> FaceTexcoords { get; set; } = new();\n        /// <summary>Additional per-corner UV channels; channel zero remains FaceTexcoords.</summary>\n        public List<MapMeshUvChannel> AdditionalUvChannels { get; set; } = new();\n    }\n\n    public sealed class MapMeshUvChannel\n    {\n        public int Channel { get; set; }\n        public string Name { get; set; } = \"UV\";\n        public List<float[][]?> FaceTexcoords { get; set; } = new();\n    }\n}\n"
if text.count(before) != 1:
    raise RuntimeError("MapMesh UV anchor mismatch")
path.write_text(text.replace(before, after, 1), encoding="utf-8", newline="\n")
