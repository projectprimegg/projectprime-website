using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace MphRead.Mods.MapGen;

/// <summary>Builds reusable Universal fidelity metadata from a validated BSP.</summary>
public static class Q3NativeFidelity
{
    public static UniversalNativeMapData Build(Q3Bsp bsp, float unitsPerUnit, string? sourceIdentity)
    {
        if (!Single.IsFinite(unitsPerUnit) || unitsPerUnit <= 0)
            throw new ArgumentOutOfRangeException(nameof(unitsPerUnit));

        var result = new UniversalNativeMapData
        {
            SourceFormat = "quake3-bsp46",
            SourceIdentity = sourceIdentity,
            LightingMode = bsp.Lightmaps.Count > 0
                ? UniversalLightingMode.SourceBakedAndRealtime
                : UniversalLightingMode.VertexFallback
        };
        result.UvSets.Add(new UniversalUvSet
        {
            Name = "Base texture",
            Channel = 0,
            Purpose = UniversalUvPurpose.BaseColor
        });
        result.UvSets.Add(new UniversalUvSet
        {
            Name = "Quake lightmap",
            Channel = 1,
            Purpose = UniversalUvPurpose.Lightmap
        });

        foreach (Q3LightmapPage page in bsp.Lightmaps)
        {
            result.Lightmaps.Add(new UniversalLightmapPage
            {
                Id = page.Id,
                Width = page.Width,
                Height = page.Height,
                Asset = $"native/q3/lightmaps/{page.Id:D4}.rgb",
                Encoding = "rgb8",
                OverbrightBits = 2,
                Exposure = 1f,
                Clamp = true,
                LinearFiltering = true
            });
        }

        for (int i = 0; i < bsp.Faces.Count; i++)
        {
            Q3Face face = bsp.Faces[i];
            result.SurfaceLighting.Add(new UniversalSurfaceLighting
            {
                SourceSurfaceId = i,
                LightmapPage = face.Lightmap,
                LightmapUvChannel = 1,
                VertexLit = face.Lightmap < 0,
                Fullbright = false
            });
        }

        for (int i = 0; i < bsp.Textures.Count; i++)
        {
            Q3Texture texture = bsp.Textures[i];
            var stack = new UniversalMaterialStack
            {
                SourceMaterial = i,
                Name = texture.Name,
                Cull = UniversalCullMode.Back
            };
            stack.Layers.Add(new UniversalMaterialLayer
            {
                Texture = texture.Name,
                UvChannel = 0,
                Blend = UniversalBlendMode.Replace
            });
            stack.Layers.Add(new UniversalMaterialLayer
            {
                Texture = "$lightmap",
                UvChannel = 1,
                Blend = UniversalBlendMode.Multiply,
                IsLightmap = true,
                DepthWrite = false
            });
            result.MaterialStacks.Add(stack);
        }

        for (int i = 0; i < bsp.Entities.Count; i++)
        {
            Dictionary<string, string> entity = bsp.Entities[i];
            entity.TryGetValue("classname", out string? className);
            result.SourceEntities.Add(new UniversalSourceEntity
            {
                SourceId = i,
                ClassName = className ?? "unknown",
                Properties = new Dictionary<string, string>(entity, StringComparer.OrdinalIgnoreCase)
            });
            if (className != null && className.Equals("light", StringComparison.OrdinalIgnoreCase)
                && entity.TryGetValue("origin", out string? origin)
                && TryVector(origin, out float[] position))
            {
                float[] color = entity.TryGetValue("_color", out string? colorText)
                    && TryVector(colorText, out float[] parsedColor)
                    ? parsedColor : new[] { 1f, 1f, 1f };
                float intensity = Number(entity, "light", 300f);
                result.SourceLights.Add(new UniversalRealtimeLight
                {
                    Name = entity.TryGetValue("targetname", out string? name) ? name : $"Quake light {i}",
                    Position = ToWorld(position, unitsPerUnit),
                    Color = color.Select(value => Math.Clamp(value, 0f, 1f)).ToArray(),
                    Intensity = MathF.Max(0, intensity / 300f),
                    Range = MathF.Max(1f, MathF.Sqrt(MathF.Max(1f, intensity)) * 2f),
                    SourceEntityId = i,
                    Style = entity.TryGetValue("style", out string? style) ? style : null
                });
            }
        }

        if (bsp.Visibility != null)
        {
            result.Visibility = new UniversalVisibilitySet
            {
                ClusterCount = bsp.Visibility.ClusterCount,
                BytesPerCluster = bsp.Visibility.BytesPerCluster,
                Asset = "native/q3/visibility.pvs"
            };
            for (int i = 0; i < bsp.Leafs.Count; i++)
            {
                Q3Leaf leaf = bsp.Leafs[i];
                var cell = new UniversalVisibilityCell
                {
                    Id = i,
                    Cluster = leaf.Cluster,
                    Area = leaf.Area,
                    Min = ToWorld(leaf.Mins.Select(value => (float)value).ToArray(), unitsPerUnit),
                    Max = ToWorld(leaf.Maxs.Select(value => (float)value).ToArray(), unitsPerUnit)
                };
                for (int surface = leaf.FirstLeafSurface;
                    surface < leaf.FirstLeafSurface + leaf.LeafSurfaceCount; surface++)
                    cell.SourceSurfaces.Add(bsp.LeafSurfaces[surface]);
                for (int brush = leaf.FirstLeafBrush;
                    brush < leaf.FirstLeafBrush + leaf.LeafBrushCount; brush++)
                    cell.SourceBrushes.Add(bsp.LeafBrushes[brush]);
                result.Visibility.Cells.Add(cell);
            }
        }

        foreach (string problem in result.Validate())
        {
            result.Diagnostics.Add(new UniversalImportDiagnostic
            {
                Code = "FP-MAP-NATIVE-001",
                Severity = "Error",
                Message = problem
            });
        }
        return result;
    }

    private static float Number(Dictionary<string, string> entity, string key, float fallback)
        => entity.TryGetValue(key, out string? text)
            && Single.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
            && Single.IsFinite(value) ? value : fallback;

    private static bool TryVector(string text, out float[] value)
    {
        string[] parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        value = new float[3];
        if (parts.Length != 3) return false;
        for (int i = 0; i < 3; i++)
            if (!Single.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out value[i])
                || !Single.IsFinite(value[i])) return false;
        return true;
    }

    private static float[] ToWorld(float[] source, float unit)
        => new[] { source[0] / unit, source[2] / unit, -source[1] / unit };
}
