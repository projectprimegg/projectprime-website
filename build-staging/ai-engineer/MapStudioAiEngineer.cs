using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MphRead.Mods.MapEditor;

namespace MphRead.Mods.Launcher.Gui;

internal sealed partial class MapStudioScreen
{
    private readonly AiMapEngineerSession _aiMapEngineer = new();
    private CancellationTokenSource? _aiMapEngineerWork;
    private TextBox? _aiMapEngineerRequest;
    private TextBox? _aiMapEngineerOutput;

    private void ShowAiMapEngineer()
    {
        if (_document == null)
        {
            _status.Text = "Open or create a map before starting AI Map Engineer.";
            return;
        }

        var view = new Grid
        {
            RowDefinitions = new("Auto,Auto,*,Auto"),
            RowSpacing = 8,
            MinWidth = 780,
            Width = 900,
            Height = 620
        };
        view.Children.Add(PrimeChrome.Stack(
            PrimeChrome.Eyebrow("UNIVERSAL MAP STUDIO // CONTROLLED TOOLING"),
            PrimeChrome.Title("AI MAP ENGINEER"),
            PrimeChrome.Text(
                "Inspect, validate, compile, audit and make reversible map edits. Mutating tools create automatic checkpoints and use normal Map Studio undo history.",
                PrimeTypography.BodySmall, PrimeTheme.TextSecondaryBrush)));

        _aiMapEngineerRequest = new TextBox
        {
            AcceptsReturn = true,
            MinHeight = 92,
            TextWrapping = TextWrapping.Wrap,
            Text = "{\n  \"tool\": \"inspect_map\",\n  \"arguments\": {}\n}"
        };
        Grid.SetRow(_aiMapEngineerRequest, 1);
        view.Children.Add(_aiMapEngineerRequest);

        _aiMapEngineerOutput = new TextBox
        {
            AcceptsReturn = true,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Text = "Run a tool to see structured results.\n\nAvailable tools:\n" +
                string.Join("\n", AiMapEngineerSession.Tools.Select(tool =>
                    $"• {tool.Name}{(tool.Mutating ? " [changes map]" : "")} — {tool.Description}"))
        };
        Grid.SetRow(_aiMapEngineerOutput, 2);
        view.Children.Add(_aiMapEngineerOutput);

        var buttons = new WrapPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetRow(buttons, 3);
        view.Children.Add(buttons);
        AddButton(buttons, "INSPECT MAP", () => RunAiQuick("inspect_map"));
        AddButton(buttons, "INSPECT SELECTION", () => RunAiQuick("inspect_selection"));
        AddButton(buttons, "VALIDATE", () => RunAiQuick("validate_map"));
        AddButton(buttons, "COMPILE", () => RunAiQuick("compile_map"));
        AddButton(buttons, "AUDIT", () => RunAiQuick("run_map_audit"));
        AddButton(buttons, "CHECKPOINT", () => RunAiQuick("create_checkpoint", new JsonObject
        {
            ["name"] = "manual-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")
        }));
        AddButton(buttons, "UNDO", () => RunAiQuick("undo"));
        AddButton(buttons, "REDO", () => RunAiQuick("redo"));
        AddButton(buttons, "RUN JSON", () => _ = RunAiRequest());
        AddButton(buttons, "CANCEL TOOL", () => _aiMapEngineerWork?.Cancel());
        AddButton(buttons, "CLOSE", Dismiss);
        Modal(view, fitContent: true);
    }

    private void RunAiQuick(string tool, JsonObject? arguments = null)
    {
        if (_aiMapEngineerRequest != null)
        {
            _aiMapEngineerRequest.Text = new JsonObject
            {
                ["tool"] = tool,
                ["arguments"] = arguments ?? new JsonObject()
            }.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        }
        _ = RunAiRequest();
    }

    private async Task RunAiRequest()
    {
        if (_document == null)
        {
            if (_aiMapEngineerOutput != null) _aiMapEngineerOutput.Text = "Open or create a map project first.";
            return;
        }
        if (_aiMapEngineerWork != null)
        {
            if (_aiMapEngineerOutput != null) _aiMapEngineerOutput.Text = "An AI Map Engineer tool is already running.";
            return;
        }

        try
        {
            JsonObject root = JsonNode.Parse(_aiMapEngineerRequest?.Text ?? string.Empty) as JsonObject
                ?? throw new InvalidOperationException("The request must be one JSON object.");
            string tool = root["tool"]?.GetValue<string>()?.Trim() ?? string.Empty;
            if (tool.Length == 0) throw new InvalidOperationException("The request needs a tool name.");
            JsonObject? arguments = root["arguments"] as JsonObject;

            _aiMapEngineerWork = new CancellationTokenSource();
            if (_aiMapEngineerOutput != null) _aiMapEngineerOutput.Text = $"Running {tool}…";
            _status.Text = "AI Map Engineer: " + tool;
            AiMapEngineerResult result = await _aiMapEngineer.ExecuteAsync(
                _document, new(tool, arguments), _aiMapEngineerWork.Token);
            if (_aiMapEngineerOutput != null) _aiMapEngineerOutput.Text = result.ToJson();
            _status.Text = result.Summary;
            if (result.Changed)
            {
                RefreshHierarchy(force: true);
                ShowInspectorPage(_inspectorPage, false);
                _viewport?.InvalidateVisual();
                RefreshStudioChrome();
            }
        }
        catch (OperationCanceledException)
        {
            if (_aiMapEngineerOutput != null) _aiMapEngineerOutput.Text = "AI Map Engineer tool cancelled.";
            _status.Text = "AI Map Engineer tool cancelled.";
        }
        catch (Exception ex)
        {
            if (_aiMapEngineerOutput != null) _aiMapEngineerOutput.Text = ex.ToString();
            Failure(ex);
        }
        finally
        {
            _aiMapEngineerWork?.Dispose();
            _aiMapEngineerWork = null;
        }
    }
}
