using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.MapEditor
{
    /// <summary>
    /// Model-agnostic, capability-scoped tool runtime for the AI Map Engineer.
    /// It deliberately exposes map operations rather than arbitrary shell/filesystem access.
    /// Mutations are routed through MapDocument history so they remain undoable.
    /// </summary>
    public sealed class AiMapEngineerSession
    {
        private const int MaxCheckpoints = 32;
        private readonly Dictionary<string, MapDefinition> _checkpoints =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Queue<string> _checkpointOrder = new();

        public static IReadOnlyList<AiMapEngineerTool> Tools { get; } = new[]
        {
            new AiMapEngineerTool("inspect_map", false, "Summarize the current map and editor state."),
            new AiMapEngineerTool("inspect_selection", false, "Inspect selected objects and positions."),
            new AiMapEngineerTool("inspect_geometry", false, "Inspect authored geometry, mesh density, layers and collision-only meshes."),
            new AiMapEngineerTool("inspect_collision", false, "Inspect dedicated/authored collision and compiled collision health when available."),
            new AiMapEngineerTool("inspect_materials", false, "Inspect material channels, animation and usage counts."),
            new AiMapEngineerTool("inspect_sources", false, "Inspect import/model/collision source provenance and packaged assets."),
            new AiMapEngineerTool("validate_map", false, "Run the shared Project Prime map validator."),
            new AiMapEngineerTool("compile_map", false, "Compile the current map in memory."),
            new AiMapEngineerTool("run_map_audit", false, "Run the existing runtime map audit harness."),
            new AiMapEngineerTool("list_problems", false, "Return the most recent validator diagnostics."),
            new AiMapEngineerTool("create_checkpoint", false, "Capture an in-memory rollback checkpoint."),
            new AiMapEngineerTool("list_checkpoints", false, "List available AI rollback checkpoints."),
            new AiMapEngineerTool("restore_checkpoint", true, "Restore a named checkpoint through undo history."),
            new AiMapEngineerTool("undo", true, "Undo the most recent editor command."),
            new AiMapEngineerTool("redo", true, "Redo the most recently undone editor command."),
            new AiMapEngineerTool("move_selection", true, "Move selected objects by a safe XYZ delta."),
            new AiMapEngineerTool("duplicate_selection", true, "Duplicate selected map objects."),
            new AiMapEngineerTool("delete_selection", true, "Delete selected objects; explicit confirm=true required."),
            new AiMapEngineerTool("rename_map", true, "Change the runtime map name after validation.")
        };

        public IReadOnlyCollection<string> Checkpoints => _checkpoints.Keys;

        public async Task<AiMapEngineerResult> ExecuteAsync(
            MapDocument document,
            AiMapEngineerRequest request,
            CancellationToken cancellation = default)
        {
            cancellation.ThrowIfCancellationRequested();
            string tool = (request.Tool ?? string.Empty).Trim().ToLowerInvariant();
            try
            {
                return tool switch
                {
                    "inspect_map" => InspectMap(document),
                    "inspect_selection" => InspectSelection(document),
                    "inspect_geometry" => InspectGeometry(document),
                    "inspect_collision" => InspectCollision(document, request.Arguments, cancellation),
                    "inspect_materials" => InspectMaterials(document),
                    "inspect_sources" => InspectSources(document),
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
                    _ => AiMapEngineerResult.Fail(tool, "Unknown or unavailable AI Map Engineer tool.")
                };
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return AiMapEngineerResult.Fail(tool, ex.Message, ex.GetType().Name);
            }
        }

        private static AiMapEngineerResult InspectMap(MapDocument document)
        {
            MapDefinition d = document.CaptureBuildSnapshot().CreateDefinition();
            return AiMapEngineerResult.Ok("inspect_map", "Map inspection complete.", new
            {
                d.Name,
                d.FormatVersion,
                geometry = d.Geometry.Count,
                legacyBrushes = d.Brushes.Count,
                materials = d.Materials.Count,
                spawns = d.Spawns.Count,
                items = d.Items.Count,
                jumpPads = d.JumpPads.Count,
                navigationLinks = d.NavigationLinks.Count,
                assets = d.Assets.Count,
                selected = document.Selection.Count,
                dirty = document.IsDirty,
                state = document.CurrentStateId.ToString()
            });
        }

        private static AiMapEngineerResult InspectSelection(MapDocument document)
        {
            MapDefinition d = document.CaptureBuildSnapshot().CreateDefinition();
            var objects = MapObjects.All(d)
                .Where(o => document.Selection.Contains(o.Id))
                .Select(o => new
                {
                    id = o.Id,
                    o.Kind,
                    o.Label,
                    position = o.Position
                }).ToArray();
            return AiMapEngineerResult.Ok("inspect_selection",
                objects.Length == 0 ? "No selected objects." : $"Inspected {objects.Length} selected object(s).",
                new { objects });
        }

        private static AiMapEngineerResult InspectGeometry(MapDocument document)
        {
            MapDefinition d = document.CaptureBuildSnapshot().CreateDefinition();
            var geometry = d.Geometry.Select(g =>
            {
                int vertices = 0, faces = 0;
                bool collisionOnly = false;
                if (g is MapMesh mesh)
                {
                    vertices = mesh.Vertices.Count;
                    faces = mesh.Faces.Count;
                    collisionOnly = mesh.CollisionOnly;
                }
                else if (g is MapConvexBrush convex)
                {
                    vertices = convex.Vertices.Count;
                    faces = convex.Faces.Count;
                }
                else if (g is MapBezierPatch patch)
                {
                    vertices = patch.ControlPoints.Count;
                    faces = Math.Max(0, patch.Columns - 1) * Math.Max(0, patch.Rows - 1);
                }

                return new
                {
                    g.Id,
                    g.Label,
                    kind = g.GetType().Name,
                    g.Layer,
                    g.Solid,
                    collisionOnly,
                    g.Material,
                    vertices,
                    faces,
                    position = g.Transform.Position,
                    scale = g.Transform.Scale
                };
            }).ToArray();

            return AiMapEngineerResult.Ok("inspect_geometry",
                $"Inspected {geometry.Length} authored geometry object(s).",
                new
                {
                    count = geometry.Length,
                    byKind = geometry.GroupBy(g => g.kind)
                        .ToDictionary(g => g.Key, g => g.Count()),
                    byLayer = geometry.GroupBy(g => g.Layer ?? "")
                        .ToDictionary(g => g.Key, g => g.Count()),
                    meshVertices = geometry.Sum(g => g.vertices),
                    meshFaces = geometry.Sum(g => g.faces),
                    collisionOnlyObjects = geometry.Count(g => g.collisionOnly),
                    objects = geometry
                });
        }

        private static AiMapEngineerResult InspectCollision(
            MapDocument document, JsonObject? args, CancellationToken cancellation)
        {
            MapDefinition d = document.CaptureBuildSnapshot().CreateDefinition();
            int authoredCollisionObjects = d.Geometry.OfType<MapMesh>().Count(m => m.CollisionOnly);
            int authoredCollisionFaces = d.Geometry.OfType<MapMesh>()
                .Where(m => m.CollisionOnly).Sum(m => m.Faces.Count);

            var source = d.Collision == null ? null : new
            {
                d.Collision.Source,
                d.Collision.ZUp,
                resolved = d.Collision.Resolve()
            };

            object? compiled = null;
            string summary = $"Found {authoredCollisionObjects} authored collision object(s) and "
                + (d.Collision == null ? "no dedicated collision source." : "a dedicated collision source.");

            if (ReadBool(args, "compile"))
            {
                MapCompilation compilation = MapCompiler.Compile(d, cancellation);
                document.Diagnostics = compilation.Validation;
                BuiltMap? map = compilation.Map;
                compiled = new
                {
                    succeeded = map != null && compilation.Validation.IsValid,
                    collisionFaces = map?.Solid.Count ?? 0,
                    importedCollisionFaces = map?.ImportedCollisionFaceCount ?? 0,
                    collisionHealth = map?.CollisionHealth,
                    repairs = map?.CollisionRepairs.Select(r => new
                    {
                        kind = r.Kind.ToString(),
                        r.Confidence,
                        r.Detail,
                        points = r.Points
                    }).ToArray(),
                    diagnostics = compilation.Validation.Diagnostics
                };
                summary = map == null
                    ? "Collision source inspected; requested compile did not produce runtime collision."
                    : $"Compiled collision contains {map.Solid.Count} face(s).";
            }

            return AiMapEngineerResult.Ok("inspect_collision", summary, new
            {
                dedicatedSource = source,
                authoredCollisionObjects,
                authoredCollisionFaces,
                compiled
            });
        }

        private static AiMapEngineerResult InspectMaterials(MapDocument document)
        {
            MapDefinition d = document.CaptureBuildSnapshot().CreateDefinition();
            int[] usage = new int[d.Materials.Count];
            foreach (MapGeometry g in d.Geometry)
            {
                if (g is MapMesh mesh && mesh.FaceMaterials.Count > 0)
                {
                    foreach (int material in mesh.FaceMaterials)
                        if (material >= 0 && material < usage.Length) usage[material]++;
                }
                else if (g.Material >= 0 && g.Material < usage.Length) usage[g.Material]++;
            }

            var materials = d.Materials.Select((m, index) => new
            {
                index,
                m.Id,
                m.Name,
                m.Texture,
                m.Albedo,
                m.Normal,
                m.SpecularRoughness,
                m.Emissive,
                m.SourceMaterial,
                m.TexScale,
                m.Alpha,
                m.TwoSided,
                animation = m.Animation,
                usages = usage[index]
            }).ToArray();

            return AiMapEngineerResult.Ok("inspect_materials",
                $"Inspected {materials.Length} material(s).",
                new
                {
                    count = materials.Length,
                    withAlbedo = materials.Count(m => !string.IsNullOrWhiteSpace(m.Albedo)),
                    withNormal = materials.Count(m => !string.IsNullOrWhiteSpace(m.Normal)),
                    withSpecularRoughness = materials.Count(m => !string.IsNullOrWhiteSpace(m.SpecularRoughness)),
                    withEmissive = materials.Count(m => !string.IsNullOrWhiteSpace(m.Emissive)),
                    animated = materials.Count(m => m.animation != null),
                    unused = materials.Count(m => m.usages == 0),
                    materials
                });
        }

        private static AiMapEngineerResult InspectSources(MapDocument document)
        {
            MapDefinition d = document.CaptureBuildSnapshot().CreateDefinition();
            return AiMapEngineerResult.Ok("inspect_sources", "Source provenance inspection complete.", new
            {
                projectPath = document.FilePath,
                d.BaseDirectory,
                d.BundlePath,
                import = d.Import,
                nativeRoom = d.NativeRoom,
                collision = d.Collision == null ? null : new
                {
                    d.Collision.Source,
                    d.Collision.ZUp,
                    resolved = d.Collision.Resolve()
                },
                modelSources = d.ModelSources,
                assets = d.Assets.Select(a => new
                {
                    a.Kind,
                    a.Path,
                    a.SourcePath
                }).ToArray()
            });
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

        private static async Task<AiMapEngineerResult> RunAudit(
            MapDocument document, CancellationToken cancellation)
        {
            MapAuditResult audit = await MapAuditRunner.Run(document.Snapshot(), cancellation)
                .ConfigureAwait(false);
            return AiMapEngineerResult.Ok("run_map_audit",
                audit.Passed ? "Runtime map audit passed." :
                    $"Runtime map audit failed with exit code {audit.ExitCode}.",
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
                $"{validation.Diagnostics.Count} diagnostic(s) currently recorded.");
        }

        private AiMapEngineerResult CreateCheckpoint(MapDocument document, JsonObject? args)
        {
            string name = ReadString(args, "name");
            if (string.IsNullOrWhiteSpace(name))
                name = "checkpoint-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            name = name.Trim();

            bool alreadyExists = _checkpoints.ContainsKey(name);
            _checkpoints[name] = document.CaptureBuildSnapshot().CreateDefinition();
            if (!alreadyExists) _checkpointOrder.Enqueue(name);

            while (_checkpointOrder.Count > MaxCheckpoints)
            {
                string oldest = _checkpointOrder.Dequeue();
                if (_checkpointOrder.Any(x => string.Equals(x, oldest, StringComparison.OrdinalIgnoreCase)))
                    continue;
                _checkpoints.Remove(oldest);
            }

            return AiMapEngineerResult.Ok("create_checkpoint", $"Checkpoint '{name}' created.",
                new { name, count = _checkpoints.Count });
        }

        private AiMapEngineerResult ListCheckpoints()
            => AiMapEngineerResult.Ok("list_checkpoints", $"{_checkpoints.Count} checkpoint(s).",
                new
                {
                    checkpoints = _checkpoints.Keys
                        .OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray()
                });

        private AiMapEngineerResult RestoreCheckpoint(MapDocument document, JsonObject? args)
        {
            string name = ReadString(args, "name");
            if (!_checkpoints.TryGetValue(name, out MapDefinition? snapshot))
                return AiMapEngineerResult.Fail("restore_checkpoint",
                    $"Checkpoint '{name}' was not found.");

            document.AiRestore("AI restore " + name, snapshot);
            return AiMapEngineerResult.Ok("restore_checkpoint", $"Checkpoint '{name}' restored.",
                new { name, state = document.CurrentStateId.ToString() });
        }

        private static AiMapEngineerResult Undo(MapDocument document)
        {
            document.History.Undo();
            return AiMapEngineerResult.Ok("undo", "Undo requested.",
                new { state = document.CurrentStateId.ToString() });
        }

        private static AiMapEngineerResult Redo(MapDocument document)
        {
            document.History.Redo();
            return AiMapEngineerResult.Ok("redo", "Redo requested.",
                new { state = document.CurrentStateId.ToString() });
        }

        private static AiMapEngineerResult MoveSelection(MapDocument document, JsonObject? args)
        {
            float[] delta = ReadVector(args, "delta");
            Guid[] ids = document.Selection.ToArray();
            if (ids.Length == 0)
                return AiMapEngineerResult.Fail("move_selection", "Nothing is selected.");

            document.Edit("AI move selection", d =>
            {
                HashSet<Guid> selected = ids.ToHashSet();
                foreach (MapObject obj in MapObjects.All(d).Where(o => selected.Contains(o.Id)))
                    obj.Move(delta);
            }, MapChangeDomain.All);

            return AiMapEngineerResult.Ok("move_selection",
                $"Moved {ids.Length} selected object(s).",
                new { delta, state = document.CurrentStateId.ToString() });
        }

        private static AiMapEngineerResult DuplicateSelection(MapDocument document)
        {
            Guid[] ids = document.Selection.ToArray();
            if (ids.Length == 0)
                return AiMapEngineerResult.Fail("duplicate_selection", "Nothing is selected.");

            document.Edit("AI duplicate selection",
                d => MapObjects.Duplicate(d, ids.ToHashSet()), MapChangeDomain.All);
            return AiMapEngineerResult.Ok("duplicate_selection",
                $"Duplicated {ids.Length} selected object(s).",
                new { state = document.CurrentStateId.ToString() });
        }

        private static AiMapEngineerResult DeleteSelection(MapDocument document, JsonObject? args)
        {
            if (!ReadBool(args, "confirm"))
                return AiMapEngineerResult.Fail("delete_selection",
                    "Deletion requires arguments.confirm=true. No change was made.");

            Guid[] ids = document.Selection.ToArray();
            if (ids.Length == 0)
                return AiMapEngineerResult.Fail("delete_selection", "Nothing is selected.");

            document.Edit("AI delete selection",
                d => MapObjects.Delete(d, ids.ToHashSet()), MapChangeDomain.All);
            return AiMapEngineerResult.Ok("delete_selection",
                $"Deleted {ids.Length} selected object(s).",
                new { state = document.CurrentStateId.ToString() });
        }

        private static AiMapEngineerResult RenameMap(MapDocument document, JsonObject? args)
        {
            string name = ReadString(args, "name").Trim();
            if (!MapValidator.ValidRuntimeName(name))
                return AiMapEngineerResult.Fail("rename_map",
                    "The requested runtime map name is invalid.");

            document.Edit("AI rename map", d => d.Name = name, MapChangeDomain.All);
            return AiMapEngineerResult.Ok("rename_map", $"Map renamed to '{name}'.",
                new { name, state = document.CurrentStateId.ToString() });
        }

        private static AiMapEngineerResult ValidationResult(
            string tool, MapValidationResult validation, string summary)
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
        {
            JsonNode? node = args?[name];
            return node == null ? string.Empty : node.ToString();
        }

        private static bool ReadBool(JsonObject? args, string name)
        {
            JsonNode? node = args?[name];
            return node != null && bool.TryParse(node.ToString(), out bool value) && value;
        }

        private static float[] ReadVector(JsonObject? args, string name)
        {
            if (args?[name] is not JsonArray array || array.Count != 3)
                throw new ArgumentException($"{name} must be a three-number array.");

            var result = new float[3];
            for (int i = 0; i < 3; i++)
            {
                string raw = array[i]?.ToString() ?? string.Empty;
                if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                    throw new ArgumentException($"{name}[{i}] is not a valid number.");
                if (!float.IsFinite(value) || Math.Abs(value) > 100000)
                    throw new ArgumentOutOfRangeException(name,
                        "Movement delta is outside the safe range.");
                result[i] = value;
            }
            return result;
        }
    }

    public sealed record AiMapEngineerTool(string Name, bool Mutates, string Description);

    public sealed record AiMapEngineerRequest(string Tool, JsonObject? Arguments = null);

    public sealed record AiMapEngineerResult(
        bool Success,
        string Tool,
        string Summary,
        JsonNode? Data,
        string? ErrorType = null)
    {
        public static AiMapEngineerResult Ok(string tool, string summary, object? data = null)
            => new(true, tool, summary,
                data == null ? null : JsonSerializer.SerializeToNode(data));

        public static AiMapEngineerResult Fail(
            string tool, string summary, string? errorType = null)
            => new(false, tool, summary, null, errorType);

        public string ToJson() => JsonSerializer.Serialize(this,
            new JsonSerializerOptions { WriteIndented = true });
    }

    public sealed partial class MapDocument
    {
        internal void AiRestore(string label, MapDefinition definition)
        {
            MapDefinition before = Project.ToDefinition();
            MapDefinition after = MapProjectSerializer.Clone(definition);
            History.Execute(new SnapshotCommand(
                this, label, before, after, MapChangeDomain.All));
        }
    }
}
