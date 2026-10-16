using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Launcher.Gui;

internal sealed partial class MapStudioScreen
{
    private StudioExperienceMode _experienceMode = StudioExperienceMode.Beginner;
    private StudioWorkflow _experienceWorkflow = StudioWorkflow.ImportExternalModel;
    private Panel? _experienceToolbar;
    private Control? _experienceMenus;
    private Control? _experienceToolsPanel;
    private Control? _experienceTreePanel;
    private Control? _experienceInspectorPanel;
    private Grid? _experienceBody;
    private bool _experienceInitialized;
    private bool _experienceRefreshing;

    private void InitializeStudioExperience(
        Panel toolbar,
        Control menus,
        Control toolsPanel,
        Grid body,
        Control treePanel,
        Control inspectorPanel)
    {
        _experienceToolbar = toolbar;
        _experienceMenus = menus;
        _experienceToolsPanel = toolsPanel;
        _experienceBody = body;
        _experienceTreePanel = treePanel;
        _experienceInspectorPanel = inspectorPanel;
        _experienceInitialized = true;

        var beginner = new PrimeButton("BEGINNER", () => SetStudioExperienceMode(StudioExperienceMode.Beginner), compact: true)
        {
            Margin = new Thickness(2), MinHeight = 28, Height = 28
        };
        var advanced = new PrimeButton("ADVANCED", () => SetStudioExperienceMode(StudioExperienceMode.Advanced), compact: true)
        {
            Margin = new Thickness(2), MinHeight = 28, Height = 28
        };
        var help = new PrimeButton("? HELP", ShowStudioHelpCatalog, compact: true)
        {
            Margin = new Thickness(2), MinHeight = 28, Height = 28
        };
        var tests = new PrimeButton("AUTO TEST", () => _ = RunStudioAcceptanceFromUi(), compact: true)
        {
            Margin = new Thickness(2), MinHeight = 28, Height = 28
        };
        ToolTip.SetTip(beginner, StudioHelpCatalog.Get("mode.beginner").Summary);
        ToolTip.SetTip(advanced, StudioHelpCatalog.Get("mode.advanced").Summary);
        ToolTip.SetTip(help, "Search plain-language explanations for Map Studio controls and workflows.");
        ToolTip.SetTip(tests, "Runs the self-contained Studio acceptance suite without requiring manual map editing.");
        ControllerNav.Identify(beginner, "studio.mode.beginner");
        ControllerNav.Identify(advanced, "studio.mode.advanced");
        ControllerNav.Identify(help, "studio.help");
        ControllerNav.Identify(tests, "studio.acceptance");
        toolbar.Children.Add(beginner);
        toolbar.Children.Add(advanced);
        toolbar.Children.Add(help);
        toolbar.Children.Add(tests);
        _editingControls.Add(beginner);
        _editingControls.Add(advanced);
        _editingControls.Add(help);
        _editingControls.Add(tests);

        ApplyStudioExperienceMode(save: false);
    }

    private void LoadStudioExperienceState()
    {
        if (Enum.TryParse(_studioState.ExperienceMode, ignoreCase: true, out StudioExperienceMode mode))
            _experienceMode = mode;
        else _experienceMode = StudioExperienceMode.Beginner;
        if (Enum.TryParse(_studioState.GuidedWorkflow, ignoreCase: true, out StudioWorkflow workflow))
            _experienceWorkflow = workflow;
        else _experienceWorkflow = StudioWorkflow.ImportExternalModel;
        ApplyStudioExperienceMode(save: false);
    }

    private void SetStudioExperienceMode(StudioExperienceMode mode)
    {
        _experienceMode = mode;
        ApplyStudioExperienceMode(save: true);
    }

    private void ApplyStudioExperienceMode(bool save)
    {
        if (!_experienceInitialized) return;
        bool beginner = _experienceMode == StudioExperienceMode.Beginner;
        if (_experienceMenus != null) _experienceMenus.IsVisible = !beginner;
        if (_experienceToolsPanel != null) _experienceToolsPanel.IsVisible = !beginner;
        if (_experienceTreePanel != null) _experienceTreePanel.IsVisible = !beginner;
        if (_experienceInspectorPanel != null) _experienceInspectorPanel.IsVisible = true;
        if (_experienceBody != null && _experienceBody.ColumnDefinitions.Count >= 5)
        {
            _experienceBody.ColumnDefinitions[0].Width = beginner ? new GridLength(0) : new GridLength(260);
            _experienceBody.ColumnDefinitions[1].Width = beginner ? new GridLength(0) : new GridLength(6);
            _experienceBody.ColumnDefinitions[2].Width = new GridLength(1, GridUnitType.Star);
            _experienceBody.ColumnDefinitions[3].Width = new GridLength(6);
            _experienceBody.ColumnDefinitions[4].Width = beginner ? new GridLength(410) : new GridLength(340);
        }

        if (beginner)
        {
            _inspectorPage = "Beginner Guide";
            ShowBeginnerGuide();
        }
        else
        {
            if (_inspectorPage == "Beginner Guide") _inspectorPage = "Inspector";
            ShowInspectorPage(_inspectorPage, remember: false);
            _status.Text = "Advanced Mode · Full modeling, collision, materials, navigation, validation and packaging controls are visible.";
        }
        if (save) SaveStudioExperienceState();
    }

    private void SaveStudioExperienceState()
    {
        if (_document == null) return;
        _studioState.ExperienceMode = _experienceMode.ToString();
        _studioState.GuidedWorkflow = _experienceWorkflow.ToString();
        try { MapStudioStateStore.Save(_document.Project.Definition, _studioState); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { _status.Text = "Could not save Studio guidance preferences: " + ex.Message; }
    }

    private StudioGuidanceSnapshot CurrentStudioGuidance()
        => StudioExperienceEngine.Evaluate(
            _document?.Project.Definition,
            _document?.FilePath,
            _document?.Diagnostics,
            _lastBuild != null,
            _experienceWorkflow,
            _studioState.CompletedGuideSteps);

    private void RefreshStudioExperience()
    {
        if (!_experienceInitialized || _experienceMode != StudioExperienceMode.Beginner) return;
        ShowBeginnerGuide();
    }

    private void ShowBeginnerGuide()
    {
        if (!_experienceInitialized || _experienceRefreshing) return;
        _experienceRefreshing = true;
        try
        {
            _inspector.Children.Clear();
            _inspectorTitle.Text = "BEGINNER GUIDE";
            var snapshot = CurrentStudioGuidance();

            _inspector.Children.Add(PrimeChrome.Eyebrow("GUIDED MAP CREATION"));
            _inspector.Children.Add(PrimeChrome.Text(
                "Tell Map Studio what you are trying to accomplish. The guide keeps every capability available while presenting the safest next step in plain language.",
                PrimeTypography.BodySmall, PrimeTheme.TextSecondaryBrush));

            var workflow = new ComboBox
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

            _inspector.Children.Add(new ProgressBar
            {
                Minimum = 0,
                Maximum = Math.Max(1, snapshot.TotalCount),
                Value = snapshot.CompleteCount,
                Height = 10,
                Margin = new Thickness(0, 8, 0, 4)
            });
            _inspector.Children.Add(PrimeChrome.Text(snapshot.Summary,
                PrimeTypography.BodySmall, snapshot.ReadyToPackage ? PrimeTheme.PrimaryBrush : PrimeTheme.TextSecondaryBrush));

            if (snapshot.NextStep is { } next)
            {
                var nextPanel = new StackPanel { Spacing = 6 };
                nextPanel.Children.Add(PrimeChrome.Eyebrow("RECOMMENDED NEXT STEP"));
                nextPanel.Children.Add(PrimeChrome.Title(next.Title.ToUpperInvariant()));
                nextPanel.Children.Add(PrimeChrome.Text(next.Summary,
                    PrimeTypography.BodySmall, PrimeTheme.TextSecondaryBrush));
                nextPanel.Children.Add(PrimeChrome.Text(next.Status,
                    PrimeTypography.DataSmall, PrimeTheme.PrimaryBrush, data: true));
                var nextButtons = new WrapPanel();
                AddExperienceActionButton(nextButtons, "DO THIS STEP", next);
                AddInfoButton(nextButtons, next.Id);
                if (next.CanMarkComplete)
                    AddButton(nextButtons, "MARK REVIEWED", () => MarkStudioStepComplete(next.Id));
                nextPanel.Children.Add(nextButtons);
                _inspector.Children.Add(new PrimePanel(nextPanel, raised: true)
                {
                    Padding = new Thickness(10), Margin = new Thickness(0, 8, 0, 8)
                });
            }

            var all = new StackPanel { Spacing = 5 };
            foreach (StudioGuidanceStep step in snapshot.Steps)
            {
                var row = new Grid { ColumnDefinitions = new("Auto,*,Auto,Auto"), ColumnSpacing = 6 };
                var state = new TextBlock
                {
                    Text = step.Complete ? "✓" : step.Blocking ? "○" : "·",
                    Foreground = step.Complete ? PrimeTheme.PrimaryBrush : PrimeTheme.TextSecondaryBrush,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 16,
                    MinWidth = 18
                };
                row.Children.Add(state);
                var copy = new StackPanel { Spacing = 1 };
                copy.Children.Add(new TextBlock
                {
                    Text = step.Title,
                    Foreground = step.Complete ? PrimeTheme.TextSecondaryBrush : GuiTheme.TextBrush,
                    FontWeight = step.Id == snapshot.NextStepId ? FontWeight.Bold : FontWeight.Normal,
                    TextWrapping = TextWrapping.Wrap
                });
                copy.Children.Add(new TextBlock
                {
                    Text = step.Status,
                    Foreground = GuiTheme.TextDimBrush,
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap
                });
                Grid.SetColumn(copy, 1); row.Children.Add(copy);
                var info = new PrimeButton("?", () => ShowStudioHelp(HelpTopicForStep(step.Id)), compact: true)
                {
                    MinWidth = 30, Width = 30, Height = 26, Margin = new Thickness(1)
                };
                ToolTip.SetTip(info, "Explain this step.");
                Grid.SetColumn(info, 2); row.Children.Add(info);
                if (!step.Complete)
                {
                    var run = new PrimeButton("GO", () => RunStudioGuidanceAction(step.ActionId), compact: true)
                    {
                        MinWidth = 42, Height = 26, Margin = new Thickness(1)
                    };
                    ToolTip.SetTip(run, step.Summary);
                    Grid.SetColumn(run, 3); row.Children.Add(run);
                }
                all.Children.Add(new Border
                {
                    BorderBrush = step.Id == snapshot.NextStepId ? PrimeTheme.PrimaryBrush : PrimeTheme.BorderBrush,
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(6),
                    Margin = new Thickness(0, 2),
                    Child = row
                });
            }
            _inspector.Children.Add(new Expander
            {
                Header = $"ALL STEPS · {snapshot.CompleteCount}/{snapshot.TotalCount}",
                IsExpanded = true,
                Content = all
            });

            var utilities = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            AddButton(utilities, "AUTOMATED TESTS", () => _ = RunStudioAcceptanceFromUi());
            AddButton(utilities, "HELP CATALOG", ShowStudioHelpCatalog);
            AddButton(utilities, "ADVANCED MODE", () => SetStudioExperienceMode(StudioExperienceMode.Advanced));
            _inspector.Children.Add(utilities);

            _status.Text = snapshot.NextStep is { } prompt
                ? $"Beginner Mode · Next: {prompt.Title} · {prompt.Status}"
                : "Beginner Mode · Workflow complete. Keep the test report with the project and package.";
        }
        finally { _experienceRefreshing = false; }
    }

    private void AddExperienceActionButton(Panel panel, string title, StudioGuidanceStep step)
    {
        var button = new PrimeButton(title, () => RunStudioGuidanceAction(step.ActionId), primary: true, compact: true)
        {
            Margin = new Thickness(2), MinHeight = 30
        };
        ToolTip.SetTip(button, step.Summary);
        panel.Children.Add(button);
    }

    private void AddInfoButton(Panel panel, string stepId)
    {
        var button = new PrimeButton("? INFO", () => ShowStudioHelp(HelpTopicForStep(stepId)), compact: true)
        {
            Margin = new Thickness(2), MinHeight = 30
        };
        ToolTip.SetTip(button, "Explain what this step does, why it matters and common mistakes.");
        panel.Children.Add(button);
    }

    private static string HelpTopicForStep(string stepId) => stepId switch
    {
        "project" => "project.new",
        "save" => "project.save",
        "import" => "import.model",
        "orientation-reviewed" => "import.zup",
        "geometry" => "geometry.blockout",
        "collision" => "collision.review",
        "spawns" => "gameplay.spawn",
        "gameplay-reviewed" => "gameplay.pickup",
        "materials" => "materials",
        "validate" => "validation",
        "runtime-audit" => "audit",
        "playtest" => "playtest",
        "build" => "export",
        "online" => "online",
        _ => "mode.beginner"
    };

    private void MarkStudioStepComplete(string id)
    {
        _studioState.CompletedGuideSteps.Add(id);
        SaveStudioExperienceState();
        ShowBeginnerGuide();
    }

    private void RunStudioGuidanceAction(string action)
    {
        switch (action)
        {
            case "create_project": NewMap(); break;
            case "open_project": Browse("Open project", false, Open, ".json", ".ppmap"); break;
            case "save_project":
                if (_document == null) NewMap();
                else if (String.IsNullOrWhiteSpace(_document.FilePath))
                    Browse("Save project", true, SaveTo, ".json");
                else Save();
                break;
            case "import_model": ImportModel(); break;
            case "create_geometry": AddObject("Box"); break;
            case "frame_all":
                _viewport?.FrameAll();
                _studioState.CompletedGuideSteps.Add("orientation-reviewed");
                SaveStudioExperienceState();
                ShowBeginnerGuide();
                break;
            case "collision_review":
                if (_viewport != null)
                {
                    _viewport.Collision = true;
                    _viewport.CollisionNormalsOverlay = false;
                    _viewport.CollisionHeatmap = false;
                    _viewport.InvalidateVisual();
                }
                ShowStudioHelp("collision.review");
                break;
            case "add_spawn": AddObject("Spawn"); break;
            case "add_pickup": AddObject("Pickup"); break;
            case "materials": _inspectorPage = "Materials"; ShowInspectorPage("Materials"); break;
            case "show_inspector": _inspectorPage = "Inspector"; ShowInspectorPage("Inspector"); break;
            case "map_health": _inspectorPage = "Map health"; ShowInspectorPage("Map health"); break;
            case "validate": _ = Validate(); break;
            case "audit":
                _studioState.CompletedGuideSteps.Add("runtime-audit");
                SaveStudioExperienceState();
                _ = Audit();
                break;
            case "playtest":
                _studioState.CompletedGuideSteps.Add("playtest");
                SaveStudioExperienceState();
                PlaytestInspector();
                break;
            case "build_package": _ = Build(true); break;
            case "online": _ = PrepareOnline(); break;
            default: ShowStudioHelpCatalog(); break;
        }
    }

    private void ShowStudioHelp(string id)
    {
        StudioHelpTopic topic = StudioHelpCatalog.Get(id);
        var panel = new StackPanel { Spacing = 8, MinWidth = 620 };
        panel.Children.Add(PrimeChrome.Eyebrow("MAP STUDIO HELP"));
        panel.Children.Add(PrimeChrome.Title(topic.Title.ToUpperInvariant()));
        panel.Children.Add(PrimeChrome.Text(topic.Summary,
            PrimeTypography.BodySmall, PrimeTheme.TextSecondaryBrush));
        if (topic.Steps.Count > 0)
        {
            panel.Children.Add(PrimeChrome.Eyebrow("HOW TO USE IT"));
            for (int i = 0; i < topic.Steps.Count; i++)
                panel.Children.Add(Text($"{i + 1}. {topic.Steps[i]}"));
        }
        if (topic.CommonMistakes.Count > 0)
        {
            panel.Children.Add(PrimeChrome.Eyebrow("COMMON MISTAKES"));
            foreach (string mistake in topic.CommonMistakes)
                panel.Children.Add(Text("• " + mistake));
        }
        AddButton(panel, "CLOSE", Dismiss);
        Modal(new ScrollViewer
        {
            Content = panel,
            MaxHeight = 560,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        }, fitContent: true);
    }

    private void ShowStudioHelpCatalog()
    {
        var root = new Grid { RowDefinitions = new("Auto,Auto,*,Auto"), RowSpacing = 8, MinWidth = 760, Height = 600 };
        root.Children.Add(PrimeChrome.Stack(
            PrimeChrome.Eyebrow("PLAIN-LANGUAGE REFERENCE"),
            PrimeChrome.Title("MAP STUDIO HELP")));
        var search = new TextBox { PlaceholderText = "Search import, collision, spawns, textures, testing…" };
        Grid.SetRow(search, 1); root.Children.Add(search);
        var list = new ListBox(); Grid.SetRow(list, 2); root.Children.Add(list);
        void Refresh()
        {
            string query = (search.Text ?? "").Trim();
            list.ItemsSource = StudioHelpCatalog.All.Where(topic => query.Length == 0
                || topic.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                || topic.Summary.Contains(query, StringComparison.OrdinalIgnoreCase)
                || topic.SearchTerms.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (list.ItemCount > 0 && list.SelectedIndex < 0) list.SelectedIndex = 0;
        }
        void OpenSelected()
        {
            if (list.SelectedItem is not StudioHelpTopic topic) return;
            Dismiss(); ShowStudioHelp(topic.Id);
        }
        list.DoubleTapped += (_, _) => OpenSelected();
        search.TextChanged += (_, _) => Refresh();
        var buttons = new WrapPanel(); Grid.SetRow(buttons, 3); root.Children.Add(buttons);
        AddButton(buttons, "OPEN", OpenSelected);
        AddButton(buttons, "CLOSE", Dismiss);
        Refresh(); Modal(root, fitContent: true); search.Focus();
    }

    private async Task RunStudioAcceptanceFromUi()
    {
        if (_work != null) return;
        await Job("Running automated Studio acceptance tests", async token =>
        {
            StudioAcceptanceReport report = await Task.Run(() => StudioExperienceAcceptance.Run(null, null, token), token);
            string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
            _status.Text = report.Passed
                ? $"Automated Studio acceptance passed · {report.PassedChecks}/{report.TotalChecks} checks"
                : $"Automated Studio acceptance failed · {report.PassedChecks}/{report.TotalChecks} checks";
            var panel = new StackPanel { Spacing = 8, MinWidth = 700 };
            panel.Children.Add(PrimeChrome.Title(report.Passed ? "AUTOMATED TESTS PASSED" : "AUTOMATED TESTS FAILED"));
            panel.Children.Add(Text(report.Summary));
            var output = new TextBox
            {
                Text = json,
                IsReadOnly = true,
                AcceptsReturn = true,
                MinHeight = 360,
                TextWrapping = TextWrapping.Wrap
            };
            panel.Children.Add(output);
            AddButton(panel, "COPY REPORT", async () =>
            {
                try
                {
#if MPHREAD_SHELL
                    unsafe { OpenTK.Windowing.GraphicsLibraryFramework.GLFW.SetClipboardString(null, json); }
#else
                    if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
                        await clipboard.SetTextAsync(json);
#endif
                    _status.Text = "Automated test report copied.";
                }
                catch (Exception ex) { _status.Text = "Could not copy report: " + ex.Message; }
            });
            AddButton(panel, "CLOSE", Dismiss);
            Modal(new ScrollViewer
            {
                Content = panel,
                MaxHeight = 620,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
            }, fitContent: true);
        });
    }

    /// <summary>
    /// Importing a model is a complete task in Beginner Mode: if no editable
    /// project exists, create and save a safe import project automatically.
    /// This removes the old silent "No project loaded" failure path.
    /// </summary>
    private void EnsureImportProject(string modelPath)
    {
        if (_document == null)
        {
            string raw = Path.GetFileNameWithoutExtension(modelPath).ToUpperInvariant();
            string safe = new(raw.Select(c => Char.IsLetterOrDigit(c) || c == '_' ? c : '_').ToArray());
            safe = String.IsNullOrWhiteSpace(safe) ? "IMPORTED_MAP" : safe;
            if (safe.Length > 32) safe = safe[..32];
            string candidate = safe + "_IMPORT";
            var project = MapTemplates.Create(candidate, "blank-arena");
            Directory.CreateDirectory(CustomRooms.UserMapDirectory);
            string path = Path.Combine(CustomRooms.UserMapDirectory, candidate.ToLowerInvariant() + ".json");
            Load(project, path);
            _document!.Save(path);
            _path.Text = path;
            _status.Text = "Created and saved an editable import project automatically.";
        }
        else if (String.IsNullOrWhiteSpace(_document.FilePath))
        {
            Directory.CreateDirectory(CustomRooms.UserMapDirectory);
            string path = Path.Combine(CustomRooms.UserMapDirectory,
                _document.Project.Definition.Name.ToLowerInvariant() + ".json");
            _document.Save(path);
            _path.Text = path;
            _status.Text = "Saved the editable project automatically before import.";
        }
    }

    internal static void AttachStudioHelp(Control control, string title)
    {
        StudioHelpTopic? topic = StudioHelpCatalog.FindForButton(title);
        if (topic != null) ToolTip.SetTip(control, topic.Summary);
    }
}
