using System;
using System.Collections.Generic;
using System.Linq;

namespace MphRead.Mods.MapGen;

/// <summary>
/// Source-independent fidelity data used by native game-map adapters.
/// Geometry, collision and gameplay stay in their existing Project Prime
/// structures; this object carries extra render and source semantics.
/// </summary>
public sealed class UniversalNativeMapData
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public string SourceFormat { get; set; } = "generic";
    public string? SourceIdentity { get; set; }
    public UniversalLightingMode LightingMode { get; set; }
        = UniversalLightingMode.SourceBakedAndRealtime;
    /// <summary>Optional render-only geometry carrying UV1 for baked lighting.</summary>
    public string? LightmapOverlayAsset { get; set; }
    public List<UniversalUvSet> UvSets { get; set; } = new();
    public List<UniversalLightmapPage> Lightmaps { get; set; } = new();
    public List<UniversalSurfaceLighting> SurfaceLighting { get; set; } = new();
    public List<UniversalMaterialStack> MaterialStacks { get; set; } = new();
    public List<UniversalRealtimeLight> SourceLights { get; set; } = new();
    public List<UniversalFogVolume> FogVolumes { get; set; } = new();
    public UniversalVisibilitySet? Visibility { get; set; }
    public List<UniversalSourceEntity> SourceEntities { get; set; } = new();
    public List<UniversalImportDiagnostic> Diagnostics { get; set; } = new();

    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();
        if (SchemaVersion is < 1 or > CurrentSchemaVersion)
            problems.Add($"Unsupported Universal native-fidelity schema {SchemaVersion}.");
        if (String.IsNullOrWhiteSpace(SourceFormat) || SourceFormat.Length > 64)
            problems.Add("SourceFormat must contain 1-64 characters.");
        if (LightmapOverlayAsset != null
            && (String.IsNullOrWhiteSpace(LightmapOverlayAsset)
                || !LightmapOverlayAsset.EndsWith(".pplm", StringComparison.OrdinalIgnoreCase)))
            problems.Add("The baked-lighting overlay asset must be a .pplm entry.");

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (UniversalUvSet uv in UvSets)
        {
            if (String.IsNullOrWhiteSpace(uv.Name) || uv.Name.Length > 64)
                problems.Add("Every UV set needs a name of 1-64 characters.");
            else if (!names.Add(uv.Name))
                problems.Add($"Duplicate UV set '{uv.Name}'.");
            if (uv.Channel is < 0 or > 7)
                problems.Add($"UV set '{uv.Name}' has unsupported channel {uv.Channel}.");
        }

        var pages = new HashSet<int>();
        foreach (UniversalLightmapPage page in Lightmaps)
        {
            if (!pages.Add(page.Id)) problems.Add($"Duplicate lightmap page {page.Id}.");
            if (page.Width is < 1 or > 8192 || page.Height is < 1 or > 8192)
                problems.Add($"Lightmap page {page.Id} has invalid dimensions.");
            if (String.IsNullOrWhiteSpace(page.Asset))
                problems.Add($"Lightmap page {page.Id} has no packaged asset.");
            if (!Single.IsFinite(page.Exposure) || page.Exposure is < 0.01f or > 32f)
                problems.Add($"Lightmap page {page.Id} has invalid exposure.");
        }

        foreach (UniversalSurfaceLighting surface in SurfaceLighting)
        {
            if (surface.SourceSurfaceId < 0)
                problems.Add("A surface-lighting binding has a negative source surface ID.");
            if (surface.LightmapPage >= 0 && !pages.Contains(surface.LightmapPage))
                problems.Add($"Surface {surface.SourceSurfaceId} references a missing lightmap.");
            if (surface.LightmapUvChannel is < 0 or > 7)
                problems.Add($"Surface {surface.SourceSurfaceId} has an invalid lightmap UV channel.");
        }

        if (SourceLights.Count > 4096) problems.Add("A map cannot contain more than 4096 source lights.");
        if (FogVolumes.Count > 1024) problems.Add("A map cannot contain more than 1024 fog volumes.");
        if (Visibility != null) problems.AddRange(Visibility.Validate());
        return problems;
    }
}

public enum UniversalLightingMode
{
    SourceBaked,
    SourceBakedAndRealtime,
    RealtimeOnly,
    VertexFallback,
    LightmapDebug
}

public sealed class UniversalUvSet
{
    public string Name { get; set; } = "UV0";
    public int Channel { get; set; }
    public UniversalUvPurpose Purpose { get; set; }
}

public enum UniversalUvPurpose
{
    BaseColor,
    Lightmap,
    Detail,
    Overlay,
    Decal,
    Custom
}

public sealed class UniversalLightmapPage
{
    public int Id { get; set; }
    public int Width { get; set; } = 128;
    public int Height { get; set; } = 128;
    public string Asset { get; set; } = "";
    public string Encoding { get; set; } = "rgb8";
    public float Exposure { get; set; } = 1f;
    public int OverbrightBits { get; set; }
    public bool Clamp { get; set; } = true;
    public bool LinearFiltering { get; set; } = true;
}

public sealed class UniversalSurfaceLighting
{
    public int SourceSurfaceId { get; set; }
    public int LightmapPage { get; set; } = -1;
    public int LightmapUvChannel { get; set; } = 1;
    public bool VertexLit { get; set; }
    public bool Fullbright { get; set; }
    public bool Emissive { get; set; }
}
