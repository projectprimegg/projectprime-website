using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
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
        // Some embedded/headless Avalonia hosts do not reliably execute the
        // native TextBox paste command even though the GLFW system clipboard
        // is available. Handle Ctrl+V explicitly here so a request copied from
        // a browser, editor or ChatGPT can always enter the AI console.
        _aiMapEngineerRequest.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.V && e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                e.Handled = true;
                await PasteAiRequestFromSystemClipboard();
            }
        };
        Grid.SetRow(_aiMapEngineerRequest, 1);
        view.Children.Add(_aiMapEngineerRequest);

        _aiMapEngineerOutput = new TextBox
        {
            AcceptsReturn = true,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            Text = "Run a tool to see structured results.\n\nAvailable tools:\n" +
                string.Join("\n", AiMapEngineerSession.Tools.Select(tool =>
                    $"• {tool.Name}{(tool.Mutating ? " [changes map]" : "")} — {tool.Description}"))
        };
        Grid.SetRow(_aiMapEngineerOutput, 2);
        view.Children.Add(_aiMapEngineerOutput);

        var buttons = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        Grid.SetRow(buttons, 3);
        view.Children.Add(buttons);
        AddButton(buttons, "PASTE FROM WINDOWS", () => _ = PasteAiRequestFromSystemClipboard());
        AddButton(buttons, "COPY RESULT", () => _ = CopyAiResultToSystemClipboard());
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

        // Put the caret in the request box immediately; the user should not
        // have to click twice before Ctrl+V can work in a modal.
        Dispatcher.UIThread.Post(() =>
        {
            _aiMapEngineerRequest?.Focus();
            _aiMapEngineerRequest?.SelectAll();
        }, DispatcherPriority.Input);
    }

    private async Task PasteAiRequestFromSystemClipboard()
    {
        if (_aiMapEngineerRequest == null) return;
        try
        {
            string? text;
#if MPHREAD_SHELL
            unsafe
            {
                text = OpenTK.Windowing.GraphicsLibraryFramework.GLFW.GetClipboardString(null);
            }
#else
            text = TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard
                ? await clipboard.TryGetTextAsync()
                : null;
#endif
            if (string.IsNullOrEmpty(text))
            {
                _status.Text = "The system clipboard does not contain text.";
                return;
            }

            _aiMapEngineerRequest.Text = text;
            _aiMapEngineerRequest.CaretIndex = text.Length;
            _aiMapEngineerRequest.Focus();
            _status.Text = "Pasted request text from the system clipboard.";
        }
        catch (Exception ex)
        {
            _status.Text = "Could not read the system clipboard: " + ex.Message;
        }
        await Task.CompletedTask;
    }

    private async Task CopyAiResultToSystemClipboard()
    {
        string text = _aiMapEngineerOutput?.Text ?? string.Empty;
        if (text.Length == 0)
        {
            _status.Text = "There is no AI Map Engineer result to copy.";
            return;
        }
        try
        {
#if MPHREAD_SHELL
            unsafe
            {
                OpenTK.Windowing.GraphicsLibraryFramework.GLFW.SetClipboardString(null, text);
            }
#else
            if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
                throw new InvalidOperationException("System clipboard is unavailable in this view.");
            await clipboard.SetTextAsync(text);
#endif
            _status.Text = "Copied AI Map Engineer result to the system clipboard.";
        }
        catch (Exception ex)
        {
            _status.Text = "Could not write to the system clipboard: " + ex.Message;
        }
        await Task.CompletedTask;
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
