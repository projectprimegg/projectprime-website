using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MphRead.Mods.MapGen;

public sealed class UniversalSourceEntity
{
    public int SourceId { get; set; }
    public string ClassName { get; set; } = "unknown";
    public Dictionary<string, string> Properties { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class UniversalImportDiagnostic
{
    public string Code { get; set; } = "UNIVERSAL";
    public string Severity { get; set; } = "Info";
    public string Message { get; set; } = "";
    public int SourceSurface { get; set; } = -1;
    public int SourceBrush { get; set; } = -1;
    public int SourceEntity { get; set; } = -1;
    public int LightmapPage { get; set; } = -1;
    public int VisibilityCluster { get; set; } = -1;
    public string? SourceShader { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SuggestedRepair { get; set; }
}
