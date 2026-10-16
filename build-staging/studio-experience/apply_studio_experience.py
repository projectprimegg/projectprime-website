from __future__ import annotations

import shutil
import sys
from pathlib import Path


def replace_once(path: Path, before: str, after: str, label: str) -> None:
    text = path.read_text(encoding="utf-8")
    count = text.count(before)
    if count != 1:
        raise RuntimeError(f"{label}: expected exactly one anchor, found {count} in {path}")
    path.write_text(text.replace(before, after, 1), encoding="utf-8", newline="\n")


def main() -> int:
    if len(sys.argv) != 3:
        raise SystemExit("usage: apply_studio_experience.py <staging-directory> <project-prime-root>")

    staging = Path(sys.argv[1]).resolve()
    root = Path(sys.argv[2]).resolve()
    editor = root / "src/MphRead/Mods/MapEditor"
    gui = root / "src/MphRead/Mods/Launcher/Gui"
    mods = root / "src/MphRead/Mods"

    for directory in (editor, gui, mods):
        if not directory.is_dir():
            raise RuntimeError(f"Project Prime source directory is missing: {directory}")

    shutil.copy2(staging / "StudioExperience.cs", editor / "StudioExperience.cs")
    shutil.copy2(staging / "StudioExperienceAcceptance.cs", editor / "StudioExperienceAcceptance.cs")
    shutil.copy2(staging / "MapStudioExperience.cs", gui / "MapStudioExperience.cs")

    state = editor / "MapStudioStateStore.cs"
    replace_once(
        state,
        '    public List<string> AcceptedCollisionRepairs { get; set; } = new();\n',
        '    public List<string> AcceptedCollisionRepairs { get; set; } = new();\n'
        '    public string ExperienceMode { get; set; } = "Beginner";\n'
        '    public string GuidedWorkflow { get; set; } = "ImportExternalModel";\n'
        '    public HashSet<string> CompletedGuideSteps { get; set; } = new(StringComparer.OrdinalIgnoreCase);\n'
        '    public bool ShowContextHelp { get; set; } = true;\n',
        "Map Studio persisted experience state",
    )

    screen = gui / "MapStudioScreen.cs"
    replace_once(
        screen,
        '        private static void AddButton(Panel panel,string title,Action action)\n'
        '        {var button=new PrimeButton(title.ToUpperInvariant(), action) {Margin=new Thickness(2),MinHeight=28,Height=28,MinWidth=60};panel.Children.Add(button);}\n',
        '        private static void AddButton(Panel panel,string title,Action action)\n'
        '        {var button=new PrimeButton(title.ToUpperInvariant(), action) {Margin=new Thickness(2),MinHeight=28,Height=28,MinWidth=60};AttachStudioHelp(button,title);panel.Children.Add(button);}\n',
        "automatic button help tooltips",
    )

    replace_once(
        screen,
        '            _editingControls.Add(body);\n'
        '            _editingControls.Add(_path);\n',
        '            _editingControls.Add(body);\n'
        '            _editingControls.Add(_path);\n'
        '            InitializeStudioExperience(toolbar,menus,toolsPanel,body,treePanel,inspectorPanel);\n',
        "Studio experience UI initialization",
    )

    replace_once(
        screen,
        '            _studioState=MapStudioStateStore.Load(project.Definition);MapStudioStateStore.Prune(project.Definition,_studioState);\n',
        '            _studioState=MapStudioStateStore.Load(project.Definition);MapStudioStateStore.Prune(project.Definition,_studioState);\n'
        '            LoadStudioExperienceState();\n',
        "load Studio experience preferences",
    )

    replace_once(
        screen,
        '                _path.Text=_document.FilePath;_status.Text="Saved "+_document.FilePath;\n'
        '                RefreshStudioChrome();\n',
        '                _path.Text=_document.FilePath;_status.Text="Saved "+_document.FilePath;\n'
        '                RefreshStudioChrome();\n'
        '                RefreshStudioExperience();\n',
        "refresh guided state after save",
    )

    replace_once(
        screen,
        '            RefreshStudioChrome();\n'
        '            _status.Text=(_document?.IsDirty==true?"Unsaved changes · ":"")\n'
        '                +"RMB orbit · MMB pan · WASD/QE fly · 1–4 modes · G/R/S transforms · E/I/B model · M merge · F fill (object mode: F focus, M measure) · Ctrl+Shift+P commands";\n',
        '            RefreshStudioChrome();\n'
        '            RefreshStudioExperience();\n'
        '            if(_experienceMode==StudioExperienceMode.Advanced)\n'
        '                _status.Text=(_document?.IsDirty==true?"Unsaved changes · ":"")\n'
        '                    +"Advanced Mode · RMB orbit · MMB pan · WASD/QE fly · 1–4 modes · G/R/S transforms · E/I/B model · M merge · F fill · Ctrl+Shift+P commands";\n',
        "guided status refresh",
    )

    replace_once(
        screen,
        '                new("Save project","file save ctrl s",Save),\n',
        '                new("Save project","file save ctrl s",Save),\n'
        '                new("Beginner Mode","guided wizard simple help",()=>SetStudioExperienceMode(StudioExperienceMode.Beginner)),\n'
        '                new("Advanced Mode","expert all settings full editor",()=>SetStudioExperienceMode(StudioExperienceMode.Advanced)),\n'
        '                new("Map Studio Help","help explain info documentation",ShowStudioHelpCatalog),\n'
        '                new("Run automated Studio tests","acceptance self test importer collision",()=>_=RunStudioAcceptanceFromUi()),\n',
        "command palette Studio experience commands",
    )

    model = gui / "MapStudioModelImport.cs"
    text = model.read_text(encoding="utf-8")
    if "using System.Threading;\n" not in text:
        text = text.replace("using System.Linq;\n", "using System.Linq;\nusing System.Threading;\n", 1)
        model.write_text(text, encoding="utf-8", newline="\n")

    replace_once(
        model,
        '    private void ImportModel() => Browse("Import OBJ / glTF / GLB model", false, path => ModelImportOptions(path), ".obj", ".gltf", ".glb");\n',
        '    private void ImportModel() => Browse("Import OBJ / glTF / GLB model", false, path => { EnsureImportProject(path); ModelImportOptions(path); }, ".obj", ".gltf", ".glb");\n',
        "automatic import project creation",
    )

    replace_once(
        model,
        '        panel.Children.Add(Text("3D MODEL · " + Path.GetFileName(path)));\n'
        '        panel.Children.Add(Text("OBJ, glTF and GLB dependencies are resolved inside the model folder. Static geometry retains materials and UV0."));\n',
        '        panel.Children.Add(Text("3D MODEL · " + Path.GetFileName(path)));\n'
        '        panel.Children.Add(Text("SOURCE · " + Path.GetFullPath(path)));\n'
        '        panel.Children.Add(Text("PROJECT · " + (_document?.FilePath ?? "automatic import project")));\n'
        '        panel.Children.Add(Text("OBJ, glTF and GLB dependencies are resolved inside the model folder. Static geometry retains materials and UV0."));\n',
        "visible import source and project paths",
    )

    replace_once(
        model,
        '    private void AnalyzeModel(string path, ModelImportSettings settings, Guid? sourceId) => _ = Job("Analyzing model", async token =>\n'
        '    {\n',
        '    private void AnalyzeModel(string path, ModelImportSettings settings, Guid? sourceId)\n'
        '    {\n'
        '        _status.Text = "Import stage 1/4 · Reading " + Path.GetFileName(path) + "…";\n'
        '        _ = Job("Analyzing model", async token =>\n'
        '        {\n',
        "staged import entry",
    )

    replace_once(
        model,
        '        var result = await Task.Run(() => ModelImportService.Import(path, settings, token), token);\n'
        '        var hash = await Task.Run(() => MapSourceFingerprint.Hash(result.Dependencies), token);\n',
        '        using var importTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);\n'
        '        importTimeout.CancelAfter(TimeSpan.FromMinutes(2));\n'
        '        ImportedModel result;\n'
        '        try\n'
        '        {\n'
        '            result = await Task.Run(() => ModelImportService.Import(path, settings, importTimeout.Token), importTimeout.Token);\n'
        '        }\n'
        '        catch(OperationCanceledException) when(!token.IsCancellationRequested)\n'
        '        {\n'
        '            throw new TimeoutException("The model importer did not finish within two minutes. The import was cancelled and no project data was changed.");\n'
        '        }\n'
        '        _status.Text = "Import stage 2/4 · Resolving dependencies and source fingerprint…";\n'
        '        var hash = await Task.Run(() => MapSourceFingerprint.Hash(result.Dependencies), token);\n'
        '        _status.Text = "Import stage 3/4 · Building preview geometry and collision…";\n',
        "import timeout and progress stages",
    )

    replace_once(
        model,
        '        panel.Children.Add(preview); preview.FrameAll();\n'
        '        panel.Children.Add(Text("Orbit and zoom to inspect. Compatible transforms, face paint and UV edits are preserved on reimport."));\n',
        '        panel.Children.Add(preview); preview.FrameAll();\n'
        '        _status.Text = "Import stage 4/4 · Preview ready. Review the model, then choose Import.";\n'
        '        panel.Children.Add(Text("Orbit and zoom to inspect. Compatible transforms, face paint and UV edits are preserved on reimport."));\n',
        "preview ready progress stage",
    )

    replace_once(
        model,
        '        AddButton(panel, "Cancel", () => { CleanupPreview(); Dismiss(); }); Modal(panel);\n'
        '    });\n'
        '}\n',
        '        AddButton(panel, "Cancel", () => { CleanupPreview(); Dismiss(); }); Modal(panel);\n'
        '        });\n'
        '    }\n'
        '}\n',
        "close staged import method",
    )

    entry = mods / "ModEntry.cs"
    anchor = '            if (HasFlag(args, "aimapengineer") || HasFlag(args, "aimapengineerhelp"))\n'
    insertion = (
        '            if (HasFlag(args, "studioexperiencecheck"))\n'
        '            {\n'
        '                Environment.ExitCode = MapEditor.StudioExperienceCheckCommand.Run(args);\n'
        '                return true;\n'
        '            }\n'
        + anchor
    )
    replace_once(entry, anchor, insertion, "headless Studio experience acceptance dispatch")

    print("Studio Experience Phase 4 integrated into", root)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
