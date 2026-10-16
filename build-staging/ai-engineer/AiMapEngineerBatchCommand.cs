using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.MapEditor
{
    /// <summary>
    /// Headless batch host for AI Map Engineer. A batch keeps one session alive so
    /// checkpoint/repair/validate/revert sequences can be executed deterministically.
    /// </summary>
    public static class AiMapEngineerBatchCommand
    {
        public static int Run(string? batchPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(batchPath))
                    throw new ArgumentException("-mapagent requires a batch JSON path.");

                string fullBatchPath = Path.GetFullPath(batchPath);
                string batchDirectory = Path.GetDirectoryName(fullBatchPath)
                    ?? Directory.GetCurrentDirectory();

                AiMapEngineerBatchSpec spec = JsonSerializer.Deserialize<AiMapEngineerBatchSpec>(
                    File.ReadAllText(fullBatchPath),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? throw new InvalidDataException("AI Map Engineer batch JSON is empty.");

                if (string.IsNullOrWhiteSpace(spec.Project))
                    throw new InvalidDataException("AI Map Engineer batch requires 'project'.");
                if (spec.Steps == null || spec.Steps.Count == 0)
                    throw new InvalidDataException("AI Map Engineer batch requires at least one step.");

                string projectPath = Resolve(batchDirectory, spec.Project);
                MapDefinition definition = MapDefinition.Load(projectPath);
                var document = new MapDocument(new MapProject(definition), projectPath);
                var session = new AiMapEngineerSession();
                var results = new List<AiMapEngineerResult>();

                foreach (AiMapEngineerBatchStep step in spec.Steps)
                {
                    AiMapEngineerResult result = session.ExecuteAsync(
                        document,
                        new AiMapEngineerRequest(step.Tool ?? string.Empty, step.Arguments))
                        .GetAwaiter().GetResult();
                    results.Add(result);

                    if (!result.Success && spec.StopOnFailure)
                        break;
                }

                bool success = results.Count == spec.Steps.Count && results.All(r => r.Success);
                string? outputPath = null;
                if (success && !string.IsNullOrWhiteSpace(spec.Output))
                {
                    outputPath = Resolve(batchDirectory, spec.Output!);
                    Directory.CreateDirectory(Path.GetDirectoryName(outputPath)
                        ?? batchDirectory);
                    document.Save(outputPath);
                }

                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    success,
                    project = projectPath,
                    output = outputPath,
                    state = document.CurrentStateId.ToString(),
                    dirty = document.IsDirty,
                    checkpoints = session.Checkpoints.OrderBy(x => x,
                        StringComparer.OrdinalIgnoreCase).ToArray(),
                    results
                }, new JsonSerializerOptions { WriteIndented = true }));

                return success ? 0 : 1;
            }
            catch (Exception ex) when (ex is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or JsonException
                or ArgumentException
                or ProgramException)
            {
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorType = ex.GetType().Name,
                    error = ex.Message
                }, new JsonSerializerOptions { WriteIndented = true }));
                return 1;
            }
        }

        private static string Resolve(string directory, string path)
            => Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(directory, path));
    }

    public sealed class AiMapEngineerBatchSpec
    {
        public string Project { get; set; } = "";
        public string? Output { get; set; }
        public bool StopOnFailure { get; set; } = true;
        public List<AiMapEngineerBatchStep> Steps { get; set; } = new();
    }

    public sealed class AiMapEngineerBatchStep
    {
        public string Tool { get; set; } = "";
        public JsonObject? Arguments { get; set; }
    }
}
