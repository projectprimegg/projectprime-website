using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Launcher.Gui;

internal sealed partial class MapStudioScreen
{
    private TextBlock? _beginnerAiSummary;
    private TextBox? _beginnerAiDetails;

    private void ShowBeginnerAiAssistant()
    {
        if (_document == null)
        {
            _status.Text = "Create or open a map before using AI Map Engineer.";
            NewMap();
            return;
        }

        var view = new Grid
        {
            RowDefinitions = new("Auto,Auto,*,Auto"),
            RowSpacing = 10,
            MinWidth = 760,
            Width = 900,
            Height = 620
        };
        view.Children.Add(PrimeChrome.Stack(
            PrimeChrome.Eyebrow("BEGINNER MODE // GUIDED AI TOOLS"),
            PrimeChrome.Title("AI MAP ENGINEER ASSISTANT"),
            PrimeChrome.Text(
                "Choose what you need help with. The assistant inspects the actual map, uses reversible editor operations, and automatically creates a checkpoint before changing anything.",
                PrimeTypography.BodySmall, PrimeTheme.TextSecondaryBrush)));

        _beginnerAiSummary = PrimeChrome.Text(
            "Start with WHAT SHOULD I DO NEXT? to let the assistant evaluate your current workflow.",
            PrimeTypography.BodySmall, PrimeTheme.PrimaryBrush);
        Grid.SetRow(_beginnerAiSummary, 1);
        view.Children.Add(_beginnerAiSummary);

        _beginnerAiDetails = new TextBox
        {
            AcceptsReturn = true,
            IsReadOnly = true,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Text = "The detailed result will appear here. Nothing is changed unless you press a clearly labeled action button."
        };
        Grid.SetRow(_beginnerAiDetails, 2);
        view.Children.Add(_beginnerAiDetails);

        var actions = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        Grid.SetRow(actions, 3);
        view.Children.Add(actions);
        AddButton(actions, "WHAT SHOULD I DO NEXT?", () => _ = RunBeginnerAi(
            "inspect_studio_guidance", BeginnerGuidanceArguments()));
        AddButton(actions, "CHECK MY MAP", () => _ = RunBeginnerAi("validate_map"));
        AddButton(actions, "CHECK LIGHTING", () => _ = RunBeginnerAi("inspect_lighting"));
        AddButton(actions, "ADD STARTER POINT LIGHT", () => _ = RunBeginnerAi("add_light", new JsonObject
        {
            ["kind"] = "Point",
            ["label"] = "Starter point light",
            ["position"] = VectorJson(_viewport?.CameraTarget ?? System.Numerics.Vector3.Zero),
            ["color"] = new JsonArray(255, 224, 192),
            ["intensity"] = 1.2f,
            ["range"] = 20f
        }));
        AddButton(actions, "MAKE THREE-POINT LIGHTING", () => _ = RunBeginnerAi("create_lighting_rig", new JsonObject
        {
            ["preset"] = "three-point",
            ["center"] = VectorJson(_viewport?.CameraTarget ?? System.Numerics.Vector3.Zero)
        }));
        AddButton(actions, "MAKE NEON LIGHTING", () => _ = RunBeginnerAi("create_lighting_rig", new JsonObject
        {
            ["preset"] = "neon-arena",
            ["center"] = VectorJson(_viewport?.CameraTarget ?? System.Numerics.Vector3.Zero)
        }));
        AddButton(actions, "OPEN LIGHTING EDITOR", () => { Dismiss(); ShowInspectorPage("Lighting"); });
        AddButton(actions, "CREATE SAFE CHECKPOINT", () => _ = RunBeginnerAi("create_checkpoint", new JsonObject
        {
            ["name"] = "beginner-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")
        }));
        AddButton(actions, "UNDO LAST CHANGE", () => _ = RunBeginnerAi("undo"));
        AddButton(actions, "ADVANCED AI CONSOLE", () => { Dismiss(); ShowAiMapEngineer(); });
        AddButton(actions, "CLOSE", Dismiss);
        Modal(view, fitContent: true);
    }

    private JsonObject BeginnerGuidanceArguments()
    {
        var completed = new JsonArray();
        foreach (string step in _studioState.CompletedGuideSteps.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
            completed.Add(step);
        return new JsonObject
        {
            ["workflow"] = _experienceWorkflow.ToString(),
            ["built"] = _lastBuild != null,
            ["completed"] = completed
        };
    }

    private static JsonArray VectorJson(System.Numerics.Vector3 value)
        => new(value.X, value.Y, value.Z);

    private async Task RunBeginnerAi(string tool, JsonObject? arguments = null)
    {
        if (_document == null || _beginnerAiSummary == null || _beginnerAiDetails == null) return;
        if (_aiMapEngineerWork != null)
        {
            _beginnerAiSummary.Text = "Another AI Map Engineer action is still running.";
            return;
        }

        try
        {
            _aiMapEngineerWork = new CancellationTokenSource();
            _beginnerAiSummary.Text = "Working: " + tool.Replace('_', ' ') + "…";
            _beginnerAiDetails.Text = "Inspecting the current project. You can cancel from the advanced console if a long audit is running.";
            AiMapEngineerResult result = await _aiMapEngineer.ExecuteAsync(
                _document, new AiMapEngineerRequest(tool, arguments), _aiMapEngineerWork.Token);
            _beginnerAiSummary.Text = result.Summary;
            _beginnerAiDetails.Text = result.ToJson();
            _status.Text = result.Summary;
            if (result.Changed)
            {
                RefreshHierarchy(force: true);
                _viewport?.InvalidateVisual();
                RefreshStudioChrome();
                RefreshStudioExperience();
            }
        }
        catch (OperationCanceledException)
        {
            _beginnerAiSummary.Text = "The AI Map Engineer action was cancelled. No partial edit was retained.";
            _beginnerAiDetails.Text = "Cancelled.";
        }
        catch (Exception ex)
        {
            _beginnerAiSummary.Text = "The assistant could not complete that action.";
            _beginnerAiDetails.Text = ex.ToString();
            Failure(ex);
        }
        finally
        {
            _aiMapEngineerWork?.Dispose();
            _aiMapEngineerWork = null;
        }
    }
}
