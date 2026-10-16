from __future__ import annotations

import sys
from pathlib import Path


def replace_once(path: Path, before: str, after: str, label: str) -> None:
    text = path.read_text(encoding="utf-8")
    count = text.count(before)
    if count != 1:
        raise RuntimeError(f"{label}: expected exactly one anchor, found {count} in {path}")
    path.write_text(text.replace(before, after, 1), encoding="utf-8", newline="\n")


def main() -> int:
    if len(sys.argv) != 2:
        raise SystemExit("usage: apply_lighting_visuals.py <project-prime-root>")
    root = Path(sys.argv[1]).resolve()
    screen = root / "src/MphRead/Mods/Launcher/Gui/MapStudioScreen.cs"
    replace_once(
        screen,
        '''                UiCapture.Capture(quad,Path.Combine(directory,"map-studio-advanced-four-views.png"),new Size(1920,1080));
            });
''',
        '''                UiCapture.Capture(quad,Path.Combine(directory,"map-studio-advanced-four-views.png"),new Size(1920,1080));

                var lighting=new MapStudioScreen();
                lighting.Load(MapTemplates.Create("Studio real-time lighting",true));
                lighting.SetStudioExperienceMode(StudioExperienceMode.Advanced);
                lighting.AddLightingRig(neon:false);
                lighting.ShowInspectorPage("Lighting");
                UiCapture.Capture(lighting,Path.Combine(directory,"map-studio-real-time-lighting.png"),new Size(1440,900));

                var assistant=new MapStudioScreen();
                assistant.Load(MapTemplates.Create("Studio beginner AI",true));
                assistant.SetStudioExperienceMode(StudioExperienceMode.Beginner);
                assistant.ShowBeginnerAiAssistant();
                UiCapture.Capture(assistant,Path.Combine(directory,"map-studio-beginner-ai-assistant.png"),new Size(1440,900));
            });
''',
        "lighting and AI visual captures",
    )
    print("Studio Lighting Phase 5 visual captures integrated into", root)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
