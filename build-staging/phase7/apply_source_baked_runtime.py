from pathlib import Path
import shutil
import sys


def replace_once(path: Path, before: str, after: str, label: str) -> None:
    text = path.read_text(encoding="utf-8")
    count = text.count(before)
    if count != 1:
        raise RuntimeError(f"{label}: expected one anchor, found {count} in {path}")
    path.write_text(text.replace(before, after, 1), encoding="utf-8", newline="\n")


if len(sys.argv) != 3:
    raise SystemExit("usage: apply_source_baked_runtime.py <staging-dir> <project-root>")
staging = Path(sys.argv[1]).resolve()
root = Path(sys.argv[2]).resolve()
render = root / "src/MphRead/Mods/Render"
shutil.copy2(staging / "SourceBakedLightingRuntime.cs", render / "SourceBakedLightingRuntime.cs")
shutil.copy2(staging / "SourceBakedLightingServer.cs", render / "SourceBakedLightingServer.cs")

room = root / "src/MphRead/Entities/RoomEntity.cs"
text = room.read_text(encoding="utf-8")
anchor = "            Mods.MapGen.MapRuntimeUsage.Track(_scene, meta.Name);\n"
if text.count(anchor) != 1:
    raise RuntimeError("RoomEntity runtime-usage anchor mismatch")
if "_scene.BindSourceBakedLighting(meta.Name);" not in text:
    text = text.replace(anchor, anchor + "            _scene.BindSourceBakedLighting(meta.Name);\n", 1)
room.write_text(text, encoding="utf-8", newline="\n")

frame = render / "FrameRenderGraph.cs"
replace_once(frame,
'''                case Mods.Render.FrameRenderPassKind.World:
                    ExecuteWorldRenderGraph();
                    break;
''',
'''                case Mods.Render.FrameRenderPassKind.World:
                    ExecuteWorldRenderGraph();
                    ApplySourceBakedLighting();
                    break;
''',
"world baked-lighting pass")

graphics = render / "GraphicsPipeline.cs"
replace_once(graphics,
'''        private void DisposeGraphicsPipeline()
        {
            ReleaseGraphicsOutputTarget();
''',
'''        private void DisposeGraphicsPipeline()
        {
            DisposeSourceBakedLighting();
            ReleaseGraphicsOutputTarget();
''',
"baked-lighting disposal")

print("Source baked-lighting OpenGL pass integrated")
