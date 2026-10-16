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
        raise SystemExit("usage: apply_studio_experience_polish.py <project-prime-root>")

    root = Path(sys.argv[1]).resolve()
    screen = root / "src/MphRead/Mods/Launcher/Gui/MapStudioScreen.cs"
    experience = root / "src/MphRead/Mods/Launcher/Gui/MapStudioExperience.cs"

    replace_once(
        screen,
        '''        internal static int Capture(string directory)
        {
            if(!GuiLauncher.EnsureSetup())return 1;
            Directory.CreateDirectory(directory);
            Dispatcher.UIThread.Invoke(()=>
            {
                var screen=new MapStudioScreen();screen.Load(MapTemplates.Create("Studio example",true));
                UiCapture.Capture(screen,Path.Combine(directory,"map-studio.png"),new Size(1440,900));
                var small=new MapStudioScreen();small.Load(MapTemplates.Create("Studio example",false));
                UiCapture.Capture(small,Path.Combine(directory,"map-studio-small.png"),new Size(960,600));
                var quad=new MapStudioScreen();quad.Load(MapTemplates.Create("Studio four views",true));quad.ToggleFourViews();
                UiCapture.Capture(quad,Path.Combine(directory,"map-studio-four-views.png"),new Size(1920,1080));
            });
            return 0;
        }
''',
        '''        internal static int Capture(string directory)
        {
            if(!GuiLauncher.EnsureSetup())return 1;
            Directory.CreateDirectory(directory);
            Dispatcher.UIThread.Invoke(()=>
            {
                var beginner=new MapStudioScreen();
                beginner.Load(MapTemplates.Create("Studio beginner example",true));
                beginner.SetStudioExperienceMode(StudioExperienceMode.Beginner);
                UiCapture.Capture(beginner,Path.Combine(directory,"map-studio-beginner.png"),new Size(1440,900));

                var beginnerSmall=new MapStudioScreen();
                beginnerSmall.Load(MapTemplates.Create("Studio beginner small",false));
                beginnerSmall.SetStudioExperienceMode(StudioExperienceMode.Beginner);
                UiCapture.Capture(beginnerSmall,Path.Combine(directory,"map-studio-beginner-small.png"),new Size(960,600));

                var advanced=new MapStudioScreen();
                advanced.Load(MapTemplates.Create("Studio advanced example",true));
                advanced.SetStudioExperienceMode(StudioExperienceMode.Advanced);
                UiCapture.Capture(advanced,Path.Combine(directory,"map-studio-advanced.png"),new Size(1440,900));

                var quad=new MapStudioScreen();
                quad.Load(MapTemplates.Create("Studio advanced four views",true));
                quad.SetStudioExperienceMode(StudioExperienceMode.Advanced);
                quad.ToggleFourViews();
                UiCapture.Capture(quad,Path.Combine(directory,"map-studio-advanced-four-views.png"),new Size(1920,1080));
            });
            return 0;
        }
''',
        "beginner and advanced visual captures",
    )

    replace_once(
        experience,
        '''    private bool _experienceInitialized;
    private bool _experienceRefreshing;
''',
        '''    private bool _experienceInitialized;
    private bool _experienceRefreshing;
    private readonly List<Control> _experienceAdvancedToolbarControls = new();
''',
        "advanced toolbar control tracking",
    )

    replace_once(
        experience,
        '''        _experienceInspectorPanel = inspectorPanel;
        _experienceInitialized = true;

        var beginner = new PrimeButton''',
        '''        _experienceInspectorPanel = inspectorPanel;
        _experienceInitialized = true;
        _experienceAdvancedToolbarControls.Clear();
        _experienceAdvancedToolbarControls.AddRange(toolbar.Children.OfType<Control>());

        var beginner = new PrimeButton''',
        "capture expert toolbar controls before guided controls",
    )

    replace_once(
        experience,
        '''        bool beginner = _experienceMode == StudioExperienceMode.Beginner;
        if (_experienceMenus != null) _experienceMenus.IsVisible = !beginner;
''',
        '''        bool beginner = _experienceMode == StudioExperienceMode.Beginner;
        foreach (Control control in _experienceAdvancedToolbarControls)
            control.IsVisible = !beginner;
        if (_experienceMenus != null) _experienceMenus.IsVisible = !beginner;
''',
        "hide advanced toolbar clutter in Beginner Mode",
    )

    replace_once(
        experience,
        '''    private void ShowBeginnerGuide()
    {
''',
        '''    private sealed record StudioWorkflowChoice(
        StudioWorkflow Value,
        string Title,
        string Description)
    {
        public override string ToString() => Title;
    }

    private static readonly StudioWorkflowChoice[] _workflowChoices =
    {
        new(StudioWorkflow.ImportExternalModel, "Import an external 3D map or environment",
            "Bring in OBJ, glTF or GLB geometry, review collision, then make it playable."),
        new(StudioWorkflow.BuildFromScratch, "Create a new map from scratch",
            "Start from a template, block out routes, add gameplay, validate and package."),
        new(StudioWorkflow.EditExistingMap, "Edit or repair an existing map",
            "Open a working copy, make a focused change, verify it and build a revision."),
        new(StudioWorkflow.PrepareForOnline, "Prepare a finished map for online play",
            "Check spawns, modes, validation, runtime behavior and the exact distributable package.")
    };

    private void ShowBeginnerGuide()
    {
''',
        "plain-language workflow choices",
    )

    replace_once(
        experience,
        '''            var workflow = new ComboBox
            {
                ItemsSource = Enum.GetValues<StudioWorkflow>(),
                SelectedItem = _experienceWorkflow,
                MinHeight = 34
            };
            ToolTip.SetTip(workflow, "Choose the workflow that best matches your goal. You can switch at any time without changing the map.");
            workflow.SelectionChanged += (_, _) =>
            {
                if (_experienceRefreshing || workflow.SelectedItem is not StudioWorkflow selected) return;
                _experienceWorkflow = selected;
                SaveStudioExperienceState();
                ShowBeginnerGuide();
            };
            _inspector.Children.Add(PrimeChrome.Text("WHAT ARE YOU TRYING TO DO?", PrimeTypography.DataSmall,
                PrimeTheme.TextSecondaryBrush, data: true));
            _inspector.Children.Add(workflow);
''',
        '''            StudioWorkflowChoice selectedWorkflow = _workflowChoices.First(choice => choice.Value == _experienceWorkflow);
            var workflow = new ComboBox
            {
                ItemsSource = _workflowChoices,
                SelectedItem = selectedWorkflow,
                MinHeight = 34
            };
            var workflowDescription = PrimeChrome.Text(selectedWorkflow.Description,
                PrimeTypography.BodySmall, PrimeTheme.TextSecondaryBrush);
            ToolTip.SetTip(workflow, "Choose the result you are trying to achieve. Switching workflows never changes the map itself.");
            workflow.SelectionChanged += (_, _) =>
            {
                if (_experienceRefreshing || workflow.SelectedItem is not StudioWorkflowChoice selected) return;
                _experienceWorkflow = selected.Value;
                workflowDescription.Text = selected.Description;
                SaveStudioExperienceState();
                ShowBeginnerGuide();
            };
            _inspector.Children.Add(PrimeChrome.Text("WHAT ARE YOU TRYING TO DO?", PrimeTypography.DataSmall,
                PrimeTheme.TextSecondaryBrush, data: true));
            _inspector.Children.Add(workflow);
            _inspector.Children.Add(workflowDescription);
''',
        "friendly workflow selector",
    )

    replace_once(
        experience,
        '''            string candidate = safe + "_IMPORT";
            var project = MapTemplates.Create(candidate, "blank-arena");
            Directory.CreateDirectory(CustomRooms.UserMapDirectory);
            string path = Path.Combine(CustomRooms.UserMapDirectory, candidate.ToLowerInvariant() + ".json");
            Load(project, path);
''',
        '''            string candidate = safe + "_IMPORT";
            Directory.CreateDirectory(CustomRooms.UserMapDirectory);
            string runtimeName = candidate;
            string path = Path.Combine(CustomRooms.UserMapDirectory, runtimeName.ToLowerInvariant() + ".json");
            int suffix = 2;
            while (File.Exists(path))
            {
                string tail = "_" + suffix++;
                string head = candidate.Length > 40 - tail.Length
                    ? candidate[..(40 - tail.Length)].TrimEnd('_')
                    : candidate;
                runtimeName = head + tail;
                path = Path.Combine(CustomRooms.UserMapDirectory, runtimeName.ToLowerInvariant() + ".json");
            }
            var project = MapTemplates.Create(runtimeName, "blank-arena");
            Load(project, path);
''',
        "duplicate-safe automatic import project",
    )

    print("Studio Experience Phase 4 polish integrated into", root)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
