using System;
using System.Collections.Generic;
using System.Linq;

namespace MphRead.Mods.MapGen;

public sealed class UniversalRealtimeLight
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Source light";
    public string Type { get; set; } = "Point";
    public float[] Position { get; set; } = new float[3];
    public float[]? Direction { get; set; }
    public float[] Color { get; set; } = new[] { 1f, 1f, 1f };
    public float Intensity { get; set; } = 1f;
    public float Range { get; set; } = 10f;
    public float InnerConeDegrees { get; set; } = 25f;
    public float OuterConeDegrees { get; set; } = 40f;
    public float Falloff { get; set; } = 2f;
    public int SourceEntityId { get; set; } = -1;
    public string? Style { get; set; }
}

public sealed class UniversalFogVolume
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Fog";
    public float[] Min { get; set; } = new float[3];
    public float[] Max { get; set; } = new float[3];
    public float[] Color { get; set; } = new[] { 0.5f, 0.5f, 0.5f };
    public float Distance { get; set; }
    public float Density { get; set; }
    public int SourceFogId { get; set; } = -1;
    public bool Enabled { get; set; } = true;
}

public sealed class UniversalVisibilitySet
{
    public int ClusterCount { get; set; }
    public int BytesPerCluster { get; set; }
    public string? Asset { get; set; }
    public List<UniversalVisibilityCell> Cells { get; set; } = new();

    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();
        if (ClusterCount is < 0 or > 65536)
            problems.Add($"Visibility cluster count {ClusterCount} is invalid.");
        if (BytesPerCluster is < 0 or > 8192)
            problems.Add($"Visibility row size {BytesPerCluster} is invalid.");
        if (ClusterCount > 0 && BytesPerCluster == 0)
            problems.Add("Visibility has clusters but no row size.");
        if (Cells.Count > 262144)
            problems.Add("Visibility contains too many cells.");
        foreach (UniversalVisibilityCell cell in Cells)
        {
            if (cell.Cluster < -1 || cell.Cluster >= Math.Max(1, ClusterCount))
                problems.Add($"Visibility cell {cell.Id} references invalid cluster {cell.Cluster}.");
            if (cell.Min.Length != 3 || cell.Max.Length != 3
                || cell.Min.Any(v => !Single.IsFinite(v))
                || cell.Max.Any(v => !Single.IsFinite(v)))
                problems.Add($"Visibility cell {cell.Id} has invalid bounds.");
        }
        return problems;
    }
}

public sealed class UniversalVisibilityCell
{
    public int Id { get; set; }
    public int Cluster { get; set; } = -1;
    public int Area { get; set; }
    public float[] Min { get; set; } = new float[3];
    public float[] Max { get; set; } = new float[3];
    public List<int> SourceSurfaces { get; set; } = new();
    public List<int> SourceBrushes { get; set; } = new();
}
