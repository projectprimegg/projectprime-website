using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using MphRead.Mods.MapEditor;

namespace MphRead.Mods.Launcher.Gui;

internal sealed partial class MapStudioScreen
{
    private readonly AiMapEngineerSession _aiMapEngineer = new();

    private void ShowAiMapEngineer()
    {
        if (_document == null)
        {
            _status.Text = "Open or create a map before starting AI Map Engineer.";
            return;
        }

        var root = new StackPanel
        {
            Spacing = 8,
            MinWidth = 760,
            MaxWidth = 920
        };

        root.Children.Add(Text("AI MAP ENGINEER"));

        root.Children.Add(new TextBlock
        {
            Text = "Controlled map-engineering tools use the same document history, validator, compiler and runtime audit as Map Studio. Mutating actions remain undoable; deletion requires explicit confirmation.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });

        var tools = new ComboBox
        {
            MinWidth = 300,
            ItemsSource = AiMapEngineerSession.Tools.Select(t => t.Name).ToArray(),
            SelectedIndex = 0
        };
        root.Children.Add(tools);

        var description = new TextBlock
        {
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        };
        root.Children.Add(description);

        void Describe()
        {
            string name = tools.SelectedItem as string ?? "";
            AiMapEngineerTool? tool = AiMapEngineerSession.Tools.FirstOrDefault(t => t.Name == name);
            description.Text = tool == null ? "" :
                (tool.Mutates ? "MUTATING · " : "READ ONLY · ") + tool.Description;
        }
        tools.SelectionChanged += (_, _) => Describe();
        Describe();

        root.Children.Add(new TextBlock { Text = "ARGUMENTS (JSON)" });
        var arguments = new TextBox
        {
            Text = "{}",
            AcceptsReturn = true,
            MinHeight = 88,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        };
        root.Children.Add(arguments);

        root.Children.Add(new TextBlock { Text = "RESULT" });
        var output = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            MinHeight = 300,
            MaxHeight = 390,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Text = "Choose a tool and run it."
        };
        root.Children.Add(output);

        async void Run(string? overrideTool = null, JsonObject? overrideArguments = null)
        {
            if (_document == null) return;
            string tool = overrideTool ?? (tools.SelectedItem as string ?? "");
            JsonObject? parsed = overrideArguments;
            if (parsed == null)
            {
                try
                {
                    string raw = string.IsNullOrWhiteSpace(arguments.Text) ? "{}" : arguments.Text!;
                    parsed = JsonNode.Parse(raw) as JsonObject
                        ?? throw new InvalidOperationException("Arguments must be a JSON object.");
                }
                catch (Exception ex)
                {
                    output.Text = "ARGUMENT ERROR\n" + ex.Message;
                    return;
                }
            }

            try
            {
                output.Text = "Running " + tool + "…";
                using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
                AiMapEngineerResult result = await _aiMapEngineer.ExecuteAsync(
                    _document, new AiMapEngineerRequest(tool, parsed), timeout.Token);
                output.Text = result.ToJson();
                _status.Text = "AI Map Engineer · " + result.Summary;
                RefreshHierarchy(force: true);
                ShowInspectorPage(_inspectorPage, false);
                _viewport?.InvalidateVisual();
            }
            catch (OperationCanceledException)
            {
                output.Text = "AI Map Engineer operation cancelled or timed out.";
            }
            catch (Exception ex)
            {
                output.Text = "AI Map Engineer failure\n" + ex;
            }
        }

        var quick = new WrapPanel
        {
            Orientation = Orientation.Horizontal
        };
        AddButton(quick, "INSPECT", () => Run("inspect_map", new JsonObject()));
        AddButton(quick, "VALIDATE", () => Run("validate_map", new JsonObject()));
        AddButton(quick, "COMPILE", () => Run("compile_map", new JsonObject()));
        AddButton(quick, "AUDIT", () => Run("run_map_audit", new JsonObject()));
        AddButton(quick, "CHECKPOINT", () => Run("create_checkpoint", new JsonObject()));
        AddButton(quick, "UNDO", () => Run("undo", new JsonObject()));
        AddButton(quick, "REDO", () => Run("redo", new JsonObject()));
        root.Children.Add(quick);

        var actions = new WrapPanel
        {
            Orientation = Orientation.Horizontal
        };
        AddButton(actions, "RUN TOOL", () => Run());
        AddButton(actions, "CLOSE", Dismiss);
        root.Children.Add(actions);

        Modal(new ScrollViewer
        {
            Content = root,
            MaxHeight = 700,
            VerticalScrollBarVisibility =
                Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        }, fitContent: true);
    }
}
