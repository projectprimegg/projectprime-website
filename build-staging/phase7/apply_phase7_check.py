from pathlib import Path
import shutil
import sys

if len(sys.argv) != 3:
    raise SystemExit("usage: apply_phase7_check.py <staging-dir> <project-root>")
staging = Path(sys.argv[1]).resolve()
root = Path(sys.argv[2]).resolve()
target = root / "tools/phase7-check"
target.mkdir(parents=True, exist_ok=True)
shutil.copy2(staging / "phase7-check.csproj", target / "phase7-check.csproj")
shutil.copy2(staging / "Phase7CheckProgram.cs", target / "Program.cs")
print("Phase 7 acceptance project staged")
