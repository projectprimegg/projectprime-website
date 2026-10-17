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
        raise SystemExit("usage: apply_zenith_phase7.py <staging-directory> <project-prime-root>")

    staging = Path(sys.argv[1]).resolve()
    root = Path(sys.argv[2]).resolve()
    universal = root / "src/MphRead/Mods/MapGen/Universal"
    gui = root / "src/MphRead/Mods/Launcher/Gui"
    mods = root / "src/MphRead/Mods"
    if not universal.is_dir():
        raise RuntimeError("UniversalMap Phase 2 must be integrated before Phase 7.")

    shutil.copy2(staging / "SourceArchiveUniversalAdapter.cs", universal / "SourceArchiveUniversalAdapter.cs")
    shutil.copy2(staging / "MapSourcePackCommand.cs", universal / "MapSourcePackCommand.cs")

    # Reject absolute/drive paths and skip directory-only entries before normalizing.
    adapter = universal / "SourceArchiveUniversalAdapter.cs"
    replace_once(
        adapter,
        '''        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            cancellation.ThrowIfCancellationRequested();
            string canonical = CanonicalEntry(entry.FullName);
            string output = Path.GetFullPath(Path.Combine(destination,
                canonical.Replace('/', Path.DirectorySeparatorChar)));
            if (!output.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Archive entry escapes extraction root: {canonical}");
            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(output);
                continue;
            }
''',
        '''        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            cancellation.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(entry.Name)) continue;
            string canonical = CanonicalEntry(entry.FullName);
            string output = Path.GetFullPath(Path.Combine(destination,
                canonical.Replace('/', Path.DirectorySeparatorChar)));
            if (!output.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Archive entry escapes extraction root: {canonical}");
''',
        "safe directory-entry extraction",
    )
    replace_once(
        adapter,
        '''        string canonical = value.Replace('\\\\', '/').TrimStart('/');
        if (canonical.Length == 0 || Path.IsPathRooted(canonical)
            || canonical.Split('/').Any(segment => segment is "" or "." or ".."))
            throw new InvalidDataException($"Unsafe archive path '{value}'.");
        return canonical;
''',
        '''        string normalized = value.Replace('\\\\', '/');
        bool driveRooted = normalized.Length >= 2
            && Char.IsLetter(normalized[0]) && normalized[1] == ':';
        if (normalized.Length == 0 || normalized.StartsWith('/', StringComparison.Ordinal)
            || driveRooted || Path.IsPathRooted(value)
            || normalized.Split('/').Any(segment => segment is "" or "." or ".."))
            throw new InvalidDataException($"Unsafe archive path '{value}'.");
        return normalized;
''',
        "absolute and traversal path rejection",
    )

    registry = universal / "UniversalMapAdapter.cs"
    replace_once(
        registry,
        '''    private static readonly List<IUniversalMapAdapter> _adapters = new()
    {
        new ExistingModelUniversalAdapter()
    };
''',
        '''    private static readonly List<IUniversalMapAdapter> _adapters = new()
    {
        new SourceArchiveUniversalAdapter(),
        new ExistingModelUniversalAdapter()
    };
''',
        "structured source-pack adapter registration",
    )

    model = gui / "MapStudioModelImport.cs"
    replace_once(
        model,
        '''    private void ImportModel() => Browse("Import OBJ / glTF / GLB model", false, path => { EnsureImportProject(path); ModelImportOptions(path); }, ".obj", ".gltf", ".glb");
''',
        '''    private void ImportModel() => Browse("Import source pack / OBJ / glTF / GLB", false,
        path => { EnsureImportProject(path); ModelImportOptions(path); }, ".zip", ".obj", ".gltf", ".glb");
''',
        "source-pack model browser",
    )
    replace_once(
        model,
        '''        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(Text("3D MODEL · " + Path.GetFileName(path)));
        panel.Children.Add(Text("SOURCE · " + Path.GetFullPath(path)));
        panel.Children.Add(Text("PROJECT · " + (_document?.FilePath ?? "automatic import project")));
        panel.Children.Add(Text("OBJ, glTF and GLB dependencies are resolved inside the model folder. Static geometry retains materials and UV0."));
        var settings = source?.Settings ?? new();
''',
        '''        var panel = new StackPanel { Spacing = 8 };
        SourceArchiveInspection? sourcePack = null;
        if (Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            try { sourcePack = SourceArchiveInspector.Inspect(path); }
            catch (Exception ex) { Failure(ex); return; }
        }
        panel.Children.Add(Text((sourcePack == null ? "3D MODEL · " : "STRUCTURED SOURCE PACK · ") + Path.GetFileName(path)));
        panel.Children.Add(Text("SOURCE · " + Path.GetFullPath(path)));
        panel.Children.Add(Text("PROJECT · " + (_document?.FilePath ?? "automatic import project")));
        panel.Children.Add(Text(sourcePack == null
            ? "OBJ, glTF and GLB dependencies are resolved inside the model folder. Static geometry retains materials and UV0."
            : "The archive is inspected by content, not mistaken for a Quake package. Supported render and collision models are routed through UniversalMap."));
        if (sourcePack != null)
        {
            panel.Children.Add(Text("Render model: " + (sourcePack.SelectedRenderModels.Count == 0
                ? "none selected" : string.Join(", ", sourcePack.SelectedRenderModels))));
            panel.Children.Add(Text("Collision model: " + (sourcePack.SelectedCollisionModels.Count == 0
                ? "none selected — certification will remain blocked" : string.Join(", ", sourcePack.SelectedCollisionModels))));
            foreach (string warning in sourcePack.Warnings.Take(8)) panel.Children.Add(Text("Warning: " + warning));
        }
        var settings = source?.Settings ?? new();
''',
        "source-pack inspection summary",
    )
    replace_once(
        model,
        '''        var collision = new ComboBox { ItemsSource = Enum.GetValues<ModelCollisionMode>(),
            SelectedItem = settings.VisualCollision ? ModelCollisionMode.Visual : settings.Collision };
''',
        '''        ModelCollisionMode initialCollision = settings.VisualCollision
            ? ModelCollisionMode.Visual
            : source != null
                ? settings.Collision
                : sourcePack?.SelectedCollisionModels.Count > 0
                    ? ModelCollisionMode.Companion
                    : settings.Collision;
        var collision = new ComboBox { ItemsSource = Enum.GetValues<ModelCollisionMode>(),
            SelectedItem = initialCollision };
''',
        "source-pack collision default",
    )
    replace_once(
        model,
        '''        string? companion = ModelCollisionImport.FindCompanion(path);
        if (companion != null) panel.Children.Add(Text("Companion detected: " + Path.GetFileName(companion)));
''',
        '''        string? companion = sourcePack == null ? ModelCollisionImport.FindCompanion(path) : null;
        if (companion != null) panel.Children.Add(Text("Companion detected: " + Path.GetFileName(companion)));
        if (sourcePack?.SelectedCollisionModels.Count > 0)
            panel.Children.Add(Text("Archive collision detected: " + string.Join(", ", sourcePack.SelectedCollisionModels)));
''',
        "source-pack collision status",
    )
    replace_once(
        model,
        '''            AddButton(sources, "Locate source…", () => Browse("Locate source model", false, path => ModelImportOptions(path, source), ".obj", ".gltf", ".glb"));
''',
        '''            AddButton(sources, "Locate source…", () => Browse("Locate source model or source pack", false,
                path => ModelImportOptions(path, source), ".zip", ".obj", ".gltf", ".glb"));
''',
        "relocate source packs",
    )

    entry = mods / "ModEntry.cs"
    anchor = '''            if (HasFlag(args, "mapcompile") || HasFlag(args, "mappackage"))
'''
    insertion = '''            if (HasFlag(args, "mapsourceinspect") || HasFlag(args, "mapsourcevalidate")
                || HasFlag(args, "sourcepackcheck"))
            {
                Environment.ExitCode = MapGen.Universal.MapSourcePackCommand.Run(args);
                return true;
            }
''' + anchor
    replace_once(entry, anchor, insertion, "headless source-pack command dispatch")

    print("Universal structured source-pack Phase 7 integrated into", root)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
