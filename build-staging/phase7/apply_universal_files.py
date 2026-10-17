from pathlib import Path
import shutil
import sys

if len(sys.argv) != 3:
    raise SystemExit("usage: apply_universal_files.py <staging-dir> <project-root>")
staging = Path(sys.argv[1]).resolve()
root = Path(sys.argv[2]).resolve()
target = root / "src/MphRead/Mods/MapGen"
for name in (
    "UniversalNativeMap.cs",
    "UniversalMaterialLayers.cs",
    "UniversalEnvironment.cs",
    "UniversalSourceMetadata.cs",
    "Q3LightmapProcessing.cs",
    "Q3LightmapPackaging.cs",
    "Q3NativeFidelity.cs",
):
    shutil.copy2(staging / name, target / name)
print("Universal contracts and Quake native-fidelity sources staged")
