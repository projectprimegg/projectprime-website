from pathlib import Path
import sys


def replace_once(path, before, after):
    text = path.read_text(encoding="utf-8")
    if text.count(before) != 1:
        raise RuntimeError(f"anchor mismatch in {path}")
    path.write_text(text.replace(before, after, 1), encoding="utf-8", newline="\n")


if len(sys.argv) != 2:
    raise SystemExit("usage: apply_mapdefinition_foundation.py <project-root>")
root = Path(sys.argv[1]).resolve()
path = root / "src/MphRead/Mods/MapGen/MapDefinition.cs"
replace_once(
    path,
    """        public MapPartitionSettings? Partitioning { get; set; }

        /// <summary>
        /// Collision read from a Wavefront OBJ, replacing whatever the
""",
    """        public MapPartitionSettings? Partitioning { get; set; }

        /// <summary>
        /// Optional source-independent baked lighting, layered materials,
        /// source lights, fog and visibility metadata. Null preserves every
        /// existing map and package exactly as before.
        /// </summary>
        public UniversalNativeMapData? NativeFidelity { get; set; }

        /// <summary>
        /// Collision read from a Wavefront OBJ, replacing whatever the
""",
)
print("MapDefinition native-fidelity property integrated")
