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
        raise SystemExit("usage: apply_studio_layout_fix.py <project-prime-root>")

    root = Path(sys.argv[1]).resolve()
    screen = root / "src/MphRead/Mods/Launcher/Gui/MapStudioScreen.cs"
    experience = root / "src/MphRead/Mods/Launcher/Gui/MapStudioExperience.cs"

    replace_once(
        screen,
        '''            var commandBar = new Grid
            {
                ColumnDefinitions = new("Auto,*,Auto"),
                ColumnSpacing = 10
            };
            commandBar.Children.Add(toolbar);
            Grid.SetColumn(_path, 1);
            commandBar.Children.Add(_path);
            var projectOps = PrimeChrome.Text(
                "PROJECT FILE  //  menus retain full import / export / online operations",
                PrimeTypography.DataSmall, PrimeTheme.TextSecondaryBrush, data: true);
            projectOps.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(projectOps, 2);
            commandBar.Children.Add(projectOps);
''',
        '''            var commandBar = new Grid
            {
                RowDefinitions = new("Auto,Auto"),
                ColumnDefinitions = new("*,Auto"),
                RowSpacing = 6,
                ColumnSpacing = 10
            };
            var toolbarScroll = new ScrollViewer
            {
                Content = toolbar,
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
            };
            Grid.SetColumnSpan(toolbarScroll, 2);
            commandBar.Children.Add(toolbarScroll);
            Grid.SetRow(_path, 1);
            Grid.SetColumn(_path, 0);
            commandBar.Children.Add(_path);
            var projectOps = PrimeChrome.Text(
                "PROJECT FILE  //  editable source and full import / export / online operations",
                PrimeTypography.DataSmall, PrimeTheme.TextSecondaryBrush, data: true);
            projectOps.VerticalAlignment = VerticalAlignment.Center;
            projectOps.TextWrapping = TextWrapping.Wrap;
            projectOps.MaxWidth = 420;
            Grid.SetRow(projectOps, 1);
            Grid.SetColumn(projectOps, 1);
            commandBar.Children.Add(projectOps);
''',
        "responsive command bar",
    )

    replace_once(
        screen,
        '            InitializeStudioExperience(toolbar,menus,toolsPanel,body,treePanel,inspectorPanel);\n',
        '            InitializeStudioExperience(toolbar,menus,toolsPanel,body,treePanel,inspectorPanel,projectOps);\n',
        "pass project copy to Studio experience",
    )

    replace_once(
        experience,
        '''    private Control? _experienceInspectorPanel;
    private Grid? _experienceBody;
''',
        '''    private Control? _experienceInspectorPanel;
    private Control? _experienceProjectOps;
    private Grid? _experienceBody;
''',
        "project copy experience field",
    )

    replace_once(
        experience,
        '''        Grid body,
        Control treePanel,
        Control inspectorPanel)
''',
        '''        Grid body,
        Control treePanel,
        Control inspectorPanel,
        Control projectOps)
''',
        "Studio experience initializer signature",
    )

    replace_once(
        experience,
        '''        _experienceTreePanel = treePanel;
        _experienceInspectorPanel = inspectorPanel;
        _experienceInitialized = true;
''',
        '''        _experienceTreePanel = treePanel;
        _experienceInspectorPanel = inspectorPanel;
        _experienceProjectOps = projectOps;
        _experienceInitialized = true;
''',
        "store project copy control",
    )

    replace_once(
        experience,
        '''        foreach (Control control in _experienceAdvancedToolbarControls)
            control.IsVisible = !beginner;
        if (_experienceMenus != null) _experienceMenus.IsVisible = !beginner;
''',
        '''        foreach (Control control in _experienceAdvancedToolbarControls)
            control.IsVisible = !beginner;
        _path.IsVisible = !beginner;
        if (_experienceProjectOps != null) _experienceProjectOps.IsVisible = !beginner;
        if (_experienceMenus != null) _experienceMenus.IsVisible = !beginner;
''',
        "hide expert project-path row in Beginner Mode",
    )

    print("Responsive Studio command-bar layout integrated into", root)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
