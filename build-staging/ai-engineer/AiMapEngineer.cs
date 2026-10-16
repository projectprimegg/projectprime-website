using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.MapEditor;

/// <summary>
/// Model-agnostic, controlled tool surface for Project Prime's AI Map Engineer.
/// It deliberately exposes map operations rather than arbitrary shell or filesystem access.
/// Every mutating tool is routed through MapDocument history and receives an automatic checkpoint.
/// </summary>
public sealed class AiMapEngineerSession
{
    private const int MaxCheckpoints = 32;
    private readonly Dictionary<string, MapDefinition> _checkpoints = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> _checkpointOrder = new();
    private int _automaticCheckpoint;

    public static IReadOnlyList<AiMapEngineerToolDefinition> Tools { get; } = new[]
    {
        new AiMapEngineerToolDefinition("describe_tools", false, "List available tools and argument contracts."),
        new AiMapEngineerToolDefinition("inspect_map", false, "Return map counts, identity, editor state and source configuration."),
        new AiMapEngineerToolDefinition("find_objects", false, "Find objects by optional kind and label text. arguments: kind, label."),
        new AiMapEngineerToolDefinition("inspect_selection", false, "Return the currently selected map objects."),
        new AiMapEngineerToolDefinition("select_objects", true, "Replace the selection. arguments: ids (GUID array)."),
        new AiMapEngineerToolDefinition("validate_map", false, "Run Project Prime source validation without requiring game assets."),
        new AiMapEngineerToolDefinition("compile_map", false, "Run Project Prime map compilation and return diagnostics/budgets."),
        new AiMapEngineerToolDefinition("run_map_audit", false, "Launch the existing timed runtime map audit harness."),
        new AiMapEngineerToolDefinition("list_problems", false, "Return the diagnostics currently stored on the document."),
        new AiMapEngineerToolDefinition("create_checkpoint", false, "Capture an in-memory named checkpoint. arguments: name."),
        new AiMapEngineerToolDefinition("list_checkpoints", false, "List session checkpoints."),
        new AiMapEngineerToolDefinition("restore_checkpoint", true, "Restore a named checkpoint through undoable history. arguments: name."),
        new AiMapEngineerToolDefinition("undo", true, "Undo the most recent document operation."),
        new AiMapEngineerToolDefinition("redo", true, "Redo the most recently undone operation."),
        new AiMapEngineerToolDefinition("move_selection", true, "Move selected objects. arguments: delta [x,y,z]."),
        new AiMapEngineerToolDefinition("duplicate_selection", true, "Duplicate the selected objects."),
        new AiMapEngineerToolDefinition("delete_selection", true, "Delete selected objects. arguments: confirm=true."),
        new AiMapEngineerToolDefinition("rename_map", true, "Set the runtime map name. arguments: name."),
    };

    public IReadOnlyCollection<string> Checkpoints => _checkpoints.Keys;

    public async Task<AiMapEngineerResult> ExecuteAsync(
        MapDocument document,
        AiMapEngineerRequest request,
        CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(request);
        cancellation.ThrowIfCancellationRequested();
        string tool = (request.Tool ?? string.Empty).Trim().ToLowerInvariant();
        AiMapEngineerToolDefinition? definition = Tools.FirstOrDefault(item => item.Name == tool);
        if (definition == null)
            return AiMapEngineerResult.Fail(tool, "Unknown or unavailable AI Map Engineer tool.");

        string? automaticCheckpoint = null;
        if (definition.Mutating && tool is not "undo" and not "redo" and not "restore_checkpoint" and not "select_objects")
            automaticCheckpoint = CreateAutomaticCheckpoint(document, tool);

        try
        {
            AiMapEngineerResult result = tool switch
            {
                "describe_tools" => DescribeTools(),
                "inspect_map" => InspectMap(document),
                "find_objects" => FindObjects(document, request.Arguments),
                "inspect_selection" => InspectSelection(document),
                "select_objects" => SelectObjects(document, request.Arguments),
                "validate_map" => ValidateMap(document),
                "compile_map" => CompileMap(document, cancellation),
                "run_map_audit" => await RunAudit(document, cancellation).ConfigureAwait(false),
                "list_problems" => ListProblems(document),
                "create_checkpoint" => CreateCheckpoint(document, request.Arguments),
                "list_checkpoints" => ListCheckpoints(),
                "restore_checkpoint" => RestoreCheckpoint(document, request.Arguments),
                "undo" => Undo(document),
                "redo" => Redo(document),
                "move_selection" => MoveSelection(document, request.Arguments),
                "duplicate_selection" => DuplicateSelection(document),
                "delete_selection" => DeleteSelection(document, request.Arguments),
                "rename_map" => RenameMap(document, request.Arguments),
                _ => AiMapEngineerResult.Fail(tool, "Tool dispatcher is incomplete.")
            };
            return result with { AutomaticCheckpoint = automaticCheckpoint };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return AiMapEngineerResult.Fail(tool, ex.Message, ex.GetType().Name)
                with { AutomaticCheckpoint = automaticCheckpoint };
        }
    }

    private static AiMapEngineerResult DescribeTools()
        => AiMapEngineerResult.Ok("describe_tools", $"{Tools.Count} controlled tools are available.",
            new { tools = Tools });

    private static AiMapEngineerResult InspectMap(MapDocument document)
    {
        MapDefinition d = document.CaptureBuildSnapshot().CreateDefinition();
        return AiMapEngineerResult.Ok("inspect_map", "Map inspection complete.", new
        {
            d.Name,
            d.InGameName,
            d.FormatVersion,
            d.MapId,
            geometry = d.Geometry.Count,
            legacyBrushes = d.Brushes.Count,
            materials = d.Materials.Count,
            spawns = d.Spawns.Count,
            items = d.Items.Count,
            jumpPads = d.JumpPads.Count,
            navigationLinks = d.NavigationLinks.Count,
            assets = d.Assets.Count,
            hasRenderImport = d.Import != null,
            hasCollisionImport = d.Collision != null,
            selected = document.Selection.Count,
            dirty = document.IsDirty,
            state = document.CurrentStateId.Value,
            filePath = document.FilePath
        });
    }

    private static AiMapEngineerResult FindObjects(MapDocument document, JsonObject? args)
    {
        string kind = ReadString(args, "kind");
        string label = ReadString(args, "label");
        MapDefinition d = document.CaptureBuildSnapshot().CreateDefinition();
        var objects = MapObjects.All(d)
            .Where(o => string.IsNullOrWhiteSpace(kind) || o.Kind.Contains(kind, StringComparison.OrdinalIgnoreCase))
            .Where(o => string.IsNullOrWhiteSpace(label) || o.Label.Contains(label, StringComparison.OrdinalIgnoreCase))
            .Take(500)
            .Select(ObjectSummary)
            .ToArray();
        return AiMapEngineerResult.Ok("find_objects", $"Found {objects.Length} matching object(s).", new { objects });
    }

    private static AiMapEngineerResult InspectSelection(MapDocument document)
    {
        MapDefinition d = document.CaptureBuildSnapshot().CreateDefinition();
        var objects = MapObjects.All(d)
            .Where(o => document.Selection.Contains(o.Id))
            .Select(ObjectSummary)
            .ToArray();
        return AiMapEngineerResult.Ok("inspect_selection",
            objects.Length == 0 ? "No objects are selected." : $"Inspected {objects.Length} selected object(s).",
            new { objects });
    }

    private static object ObjectSummary(MapObject o) => new
    {
        id = o.Id,
        o.Kind,
        o.Label,
        position = o.Position
    };

    private static AiMapEngineerResult SelectObjects(MapDocument document, JsonObject? args)
    {
        Guid[] ids = ReadGuids(args, "ids");
        HashSet<Guid> existing = MapObjects.All(document.Project.Definition).Select(o => o.Id).ToHashSet();
        Guid[] accepted = ids.Where(existing.Contains).Distinct().ToArray();
        document.Selection.Clear();
        foreach (Guid id in accepted) document.Selection.Add(id);
        document.ActiveObjectId = accepted.FirstOrDefault();
        document.SelectionChanged();
        return AiMapEngineerResult.Ok("select_objects", $"Selected {accepted.Length} object(s).",
            new { accepted, rejected = ids.Except(accepted).ToArray() }, changed: true);
    }

    private static AiMapEngineerResult ValidateMap(MapDocument document)
    {
        MapDefinition d = document.CaptureBuildSnapshot().CreateDefinition();
        MapValidationResult validation = MapValidator.Validate(d, checkSources: false);
        document.Diagnostics = validation;
        return ValidationResult("validate_map", validation,
            validation.IsValid ? "Validation passed." : "Validation found blocking errors.");
    }

    private static AiMapEngineerResult CompileMap(MapDocument document, CancellationToken cancellation)
    {
        MapDefinition d = document.CaptureBuildSnapshot().CreateDefinition();
        MapCompilation compilation = MapCompiler.Compile(d, cancellation);
        document.Diagnostics = compilation.Validation;
        return ValidationResult("compile_map", compilation.Validation,
            compilation.Map != null && compilation.Validation.IsValid
                ? "Map compiled successfully."
                : "Compilation did not produce a runtime map.");
    }

    private static async Task<AiMapEngineerResult> RunAudit(MapDocument document, CancellationToken cancellation)
    {
        MapAuditResult audit = await MapAuditRunner.Run(document.Snapshot(), cancellation).ConfigureAwait(false);
        return AiMapEngineerResult.Ok("run_map_audit",
            audit.Passed ? "Runtime map audit passed." : $"Runtime map audit failed with exit code {audit.ExitCode}.",
            new
            {
                audit.Passed,
                audit.ExitCode,
                lines = audit.Lines.TakeLast(250).ToArray()
            });
    }

    private static AiMapEngineerResult ListProblems(MapDocument document)
    {
        MapValidationResult validation = document.Diagnostics;
        return ValidationResult("list_problems", validation,
            $"{validation.Diagnostics.Count} diagnostic(s) are currently recorded.");
    }

    private AiMapEngineerResult CreateCheckpoint(MapDocument document, JsonObject? args)
    {
        string name = ReadString(args, "name");
        if (string.IsNullOrWhiteSpace(name))
            name = "checkpoint-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        name = name.Trim();
        StoreCheckpoint(name, document.CaptureBuildSnapshot().CreateDefinition());
        return AiMapEngineerResult.Ok("create_checkpoint", $"Checkpoint '{name}' created.",
            new { name, count = _checkpoints.Count });
    }

    private string CreateAutomaticCheckpoint(MapDocument document, string tool)
    {
        string name = $"auto-{++_automaticCheckpoint:D3}-{tool}";
        StoreCheckpoint(name, document.CaptureBuildSnapshot().CreateDefinition());
        return name;
    }

    private void StoreCheckpoint(string name, MapDefinition definition)
    {
        if (!_checkpoints.ContainsKey(name)) _checkpointOrder.Enqueue(name);
        _checkpoints[name] = MapProjectSerializer.Clone(definition);
        while (_checkpointOrder.Count > MaxCheckpoints)
        {
            string oldest = _checkpointOrder.Dequeue();
            if (_checkpointOrder.Any(item => string.Equals(item, oldest, StringComparison.OrdinalIgnoreCase))) continue;
            _checkpoints.Remove(oldest);
        }
    }

    private AiMapEngineerResult ListCheckpoints()
        => AiMapEngineerResult.Ok("list_checkpoints", $"{_checkpoints.Count} checkpoint(s).",
            new { checkpoints = _checkpoints.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray() });

    private AiMapEngineerResult RestoreCheckpoint(MapDocument document, JsonObject? args)
    {
        string name = ReadString(args, "name");
        if (!_checkpoints.TryGetValue(name, out MapDefinition? snapshot))
            return AiMapEngineerResult.Fail("restore_checkpoint", $"Checkpoint '{name}' was not found.");
        document.AiRestore("AI restore " + name, snapshot);
        return AiMapEngineerResult.Ok("restore_checkpoint", $"Checkpoint '{name}' restored.",
            new { name, state = document.CurrentStateId.Value }, changed: true);
    }

    private static AiMapEngineerResult Undo(MapDocument document)
    {
        bool changed = document.History.CanUndo;
        document.History.Undo();
        return AiMapEngineerResult.Ok("undo", changed ? "Undo completed." : "Nothing was available to undo.",
            new { state = document.CurrentStateId.Value }, changed);
    }

    private static AiMapEngineerResult Redo(MapDocument document)
    {
        bool changed = document.History.CanRedo;
        document.History.Redo();
        return AiMapEngineerResult.Ok("redo", changed ? "Redo completed." : "Nothing was available to redo.",
            new { state = document.CurrentStateId.Value }, changed);
    }

    private static AiMapEngineerResult MoveSelection(MapDocument document, JsonObject? args)
    {
        float[] delta = ReadVector(args, "delta");
        Guid[] ids = document.Selection.ToArray();
        if (ids.Length == 0) return AiMapEngineerResult.Fail("move_selection", "Nothing is selected.");
        document.Edit("AI move selection", d =>
        {
            HashSet<Guid> selected = ids.ToHashSet();
            foreach (MapObject obj in MapObjects.All(d).Where(o => selected.Contains(o.Id))) obj.Move(delta);
        }, MapChangeDomain.All);
        return AiMapEngineerResult.Ok("move_selection", $"Moved {ids.Length} selected object(s).",
            new { delta, state = document.CurrentStateId.Value }, changed: true);
    }

    private static AiMapEngineerResult DuplicateSelection(MapDocument document)
    {
        Guid[] ids = document.Selection.ToArray();
        if (ids.Length == 0) return AiMapEngineerResult.Fail("duplicate_selection", "Nothing is selected.");
        document.Edit("AI duplicate selection", d => MapObjects.Duplicate(d, ids.ToHashSet()), MapChangeDomain.All);
        return AiMapEngineerResult.Ok("duplicate_selection", $"Duplicated {ids.Length} selected object(s).",
            new { state = document.CurrentStateId.Value }, changed: true);
    }

    private static AiMapEngineerResult DeleteSelection(MapDocument document, JsonObject? args)
    {
        if (!ReadBool(args, "confirm"))
            return AiMapEngineerResult.Fail("delete_selection", "Deletion requires arguments.confirm=true. No change was made.");
        Guid[] ids = document.Selection.ToArray();
        if (ids.Length == 0) return AiMapEngineerResult.Fail("delete_selection", "Nothing is selected.");
        document.Edit("AI delete selection", d => MapObjects.Delete(d, ids.ToHashSet()), MapChangeDomain.All);
        return AiMapEngineerResult.Ok("delete_selection", $"Deleted {ids.Length} selected object(s).",
            new { state = document.CurrentStateId.Value }, changed: true);
    }

    private static AiMapEngineerResult RenameMap(MapDocument document, JsonObject? args)
    {
        string name = ReadString(args, "name").Trim();
        if (!MapValidator.ValidRuntimeName(name))
            return AiMapEngineerResult.Fail("rename_map", "The requested runtime map name is invalid.");
        document.Edit("AI rename map", d => d.Name = name, MapChangeDomain.Metadata);
        return AiMapEngineerResult.Ok("rename_map", $"Map renamed to '{name}'.",
            new { name, state = document.CurrentStateId.Value }, changed: true);
    }

    private static AiMapEngineerResult ValidationResult(string tool, MapValidationResult validation, string summary)
        => AiMapEngineerResult.Ok(tool, summary, new
        {
            valid = validation.IsValid,
            diagnostics = validation.Diagnostics.Select(d => new
            {
                d.Code,
                severity = d.Severity.ToString(),
                d.Message,
                d.ObjectId
            }).ToArray(),
            budgets = validation.Budgets.Select(b => new
            {
                b.Name,
                b.Used,
                b.Limit,
                b.Percent
            }).ToArray()
        });

    private static string ReadString(JsonObject? args, string name)
        => args?[name]?.GetValue<string>() ?? string.Empty;

    private static bool ReadBool(JsonObject? args, string name)
        => args?[name]?.GetValue<bool>() ?? false;

    private static float[] ReadVector(JsonObject? args, string name)
    {
        if (args?[name] is not JsonArray array || array.Count != 3)
            throw new ArgumentException($"{name} must be a three-number array.");
        var result = new float[3];
        for (int i = 0; i < 3; i++)
        {
            result[i] = array[i]?.GetValue<float>() ?? throw new ArgumentException($"{name}[{i}] is missing.");
            if (!float.IsFinite(result[i]) || Math.Abs(result[i]) > 100000)
                throw new ArgumentOutOfRangeException(name, "Vector component is outside the safe range.");
        }
        return result;
    }

    private static Guid[] ReadGuids(JsonObject? args, string name)
    {
        if (args?[name] is not JsonArray array) throw new ArgumentException($"{name} must be a GUID array.");
        var result = new List<Guid>();
        foreach (JsonNode? node in array)
        {
            string value = node?.GetValue<string>() ?? string.Empty;
            if (!Guid.TryParse(value, out Guid id)) throw new ArgumentException($"'{value}' is not a valid GUID.");
            result.Add(id);
        }
        return result.ToArray();
    }
}

public sealed record AiMapEngineerToolDefinition(string Name, bool Mutating, string Description);
public sealed record AiMapEngineerRequest(string Tool, JsonObject? Arguments = null);

public sealed record AiMapEngineerResult(
    bool Success,
    string Tool,
    string Summary,
    JsonNode? Data,
    bool Changed = false,
    string? ErrorType = null,
    string? AutomaticCheckpoint = null)
{
    public static AiMapEngineerResult Ok(string tool, string summary, object? data = null, bool changed = false)
        => new(true, tool, summary, data == null ? null : JsonSerializer.SerializeToNode(data), changed);

    public static AiMapEngineerResult Fail(string tool, string summary, string? errorType = null)
        => new(false, tool, summary, null, false, errorType);

    public string ToJson() => JsonSerializer.Serialize(this,
        new JsonSerializerOptions { WriteIndented = true });
}

public static class AiMapEngineerCommand
{
    public static int Run(string[] args)
    {
        try
        {
            if (HasFlag(args, "aimapengineerhelp"))
            {
                Console.WriteLine(JsonSerializer.Serialize(new { tools = AiMapEngineerSession.Tools },
                    new JsonSerializerOptions { WriteIndented = true }));
                return 0;
            }

            int at = IndexOfFlag(args, "aimapengineer");
            if (at < 0 || at + 1 >= args.Length)
            {
                Console.Error.WriteLine("Usage: -aimapengineer <project.json> -aitool <tool> [-aiargs <json>] [-aiwrite|-aioutputproject <path>]");
                return 2;
            }

            string projectPath = Path.GetFullPath(args[at + 1]);
            string tool = ValueAfter(args, "aitool") ?? "inspect_map";
            JsonObject? arguments = null;
            if (ValueAfter(args, "aiargs") is string rawArgs)
                arguments = JsonNode.Parse(rawArgs) as JsonObject
                    ?? throw new InvalidDataException("-aiargs must contain one JSON object.");
            if (ValueAfter(args, "airequest") is string requestPath)
            {
                JsonObject request = JsonNode.Parse(File.ReadAllText(requestPath)) as JsonObject
                    ?? throw new InvalidDataException("AI request file must contain one JSON object.");
                tool = request["tool"]?.GetValue<string>() ?? tool;
                arguments = request["arguments"] as JsonObject;
            }

            var document = new MapDocument(new MapProject(MapDefinition.Load(projectPath)), projectPath);
            var session = new AiMapEngineerSession();
            AiMapEngineerResult result = session.ExecuteAsync(document, new(tool, arguments)).GetAwaiter().GetResult();
            Console.WriteLine(result.ToJson());

            if (result.Success && result.Changed)
            {
                string? output = ValueAfter(args, "aioutputproject");
                if (output != null) document.Save(Path.GetFullPath(output));
                else if (HasFlag(args, "aiwrite")) document.Save(projectPath);
            }
            return result.Success ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(JsonSerializer.Serialize(new
            {
                success = false,
                errorType = ex.GetType().Name,
                error = ex.Message
            }, new JsonSerializerOptions { WriteIndented = true }));
            return 1;
        }
    }

    private static bool HasFlag(string[] args, string flag) => IndexOfFlag(args, flag) >= 0;

    private static int IndexOfFlag(string[] args, string flag)
    {
        string normalized = flag.TrimStart('-');
        return Array.FindIndex(args, item => item.TrimStart('-').Equals(normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static string? ValueAfter(string[] args, string flag)
    {
        int index = IndexOfFlag(args, flag);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}

public sealed partial class MapDocument
{
    internal void AiRestore(string label, MapDefinition definition)
    {
        MapDefinition before = Project.ToDefinition();
        MapDefinition after = MapProjectSerializer.Clone(definition);
        History.Execute(new SnapshotCommand(this, label, before, after, MapChangeDomain.All));
    }
}
