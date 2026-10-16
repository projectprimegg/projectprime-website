using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading;
using OpenTK.Mathematics;

namespace MphRead.Mods.MapGen;

/// <summary>
/// A compact, packageable scene asset for very large imported environments.
/// Render and collision meshes remain separate inside one bounded binary file,
/// avoiding enormous project.json files without hiding source fidelity loss.
/// </summary>
public sealed class MapPackedScene
{
    public string Source { get; set; } = "";
    public bool AutoHealCollision { get; set; } = true;
    public float CollisionHealTolerance { get; set; } = 0.0625f;
    public bool RequireAllHunterForms { get; set; } = true;

    [JsonIgnore] public string? BaseDirectory { get; set; }
    [JsonIgnore] public string? BundlePath { get; set; }

    public byte[]? ReadBytes()
    {
        if (String.IsNullOrWhiteSpace(Source)) return null;
        if (BundlePath != null)
            return MapBundle.ReadEntry(BundlePath, Source)
                ?? throw new InvalidDataException("Packaged scene asset is missing.");
        string? path = Resolve();
        if (path == null) return null;
        var info = new FileInfo(path);
        if (info.Length > MapPackageReader.MaxEntryBytes)
            throw new InvalidDataException("Packed scene exceeds the package entry limit.");
        return File.ReadAllBytes(path);
    }

    public string? Resolve()
    {
        if (String.IsNullOrWhiteSpace(Source)) return null;
        if (Path.IsPathRooted(Source)) return File.Exists(Source) ? Source : null;
        foreach (string candidate in new[]
        {
            BaseDirectory == null ? null : Path.Combine(BaseDirectory, Source),
            Source,
            Path.Combine(CustomRooms.MapDirectory, Source),
            Path.Combine(Mods.Launcher.GameFiles.Root, Source)
        }.OfType<string>())
        {
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}

[Flags]
public enum MapPackedMeshFlags
{
    None = 0,
    Solid = 1,
    CollisionOnly = 2,
    Damaging = 4,
    IgnorePlayers = 8,
    IgnoreBeams = 16,
    IgnoreScan = 32,
    ReflectBeams = 64,
    Sky = 128
}

public sealed record MapPackedSceneSummary(
    int Meshes,
    int RenderFaces,
    int CollisionFaces,
    int Vertices,
    long Bytes);

/// <summary>Strict reader for the PPMESH01 interchange used by large-scene import.</summary>
public static class MapPackedSceneCodec
{
    public const string Magic = "PPMESH01";
    public const int Version = 1;
    public const int MaxMeshes = 4096;
    public const int MaxVertices = 4_000_000;
    public const int MaxFaces = 4_000_000;

    public static MapPackedSceneSummary AddTo(BuiltMap map, MapDefinition definition,
        MapPackedScene source, CancellationToken cancellation = default, int materialOffset = 0)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(source);
        byte[] bytes = source.ReadBytes()
            ?? throw new InvalidDataException($"Packed scene '{source.Source}' was not found.");
        using var memory = new MemoryStream(bytes, writable: false);
        using var reader = new BinaryReader(memory, Encoding.UTF8, leaveOpen: false);
        string magic = Encoding.ASCII.GetString(reader.ReadBytes(8));
        if (magic != Magic) throw new InvalidDataException("Packed scene has an invalid PPMESH header.");
        int version = reader.ReadInt32();
        if (version != Version) throw new InvalidDataException($"Packed scene version {version} is unsupported.");
        int meshCount = reader.ReadInt32();
        if (meshCount is < 1 or > MaxMeshes) throw new InvalidDataException("Packed scene mesh count exceeds limits.");

        int totalVertices = 0, totalFaces = 0, renderFaces = 0, collisionFaces = 0;
        for (int meshIndex = 0; meshIndex < meshCount; meshIndex++)
        {
            cancellation.ThrowIfCancellationRequested();
            int material = reader.ReadInt32();
            MapPackedMeshFlags flags = (MapPackedMeshFlags)reader.ReadInt32();
            int terrainValue = reader.ReadInt32();
            int vertexCount = reader.ReadInt32();
            int faceCount = reader.ReadInt32();
            int labelBytes = reader.ReadInt32();
            if (vertexCount is < 3 or > MaxVertices || faceCount is < 1 or > MaxFaces
                || labelBytes is < 0 or > 4096)
                throw new InvalidDataException("Packed scene mesh dimensions exceed limits.");
            if (checked(totalVertices + vertexCount) > MaxVertices
                || checked(totalFaces + faceCount) > MaxFaces)
                throw new InvalidDataException("Packed scene total geometry exceeds limits.");
            string label = Encoding.UTF8.GetString(reader.ReadBytes(labelBytes));
            if (label.Length > 512) throw new InvalidDataException("Packed scene mesh label is too long.");

            var vertices = new Vector3[vertexCount];
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 value = new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z)
                    || MathF.Abs(value.X) >= 524288 || MathF.Abs(value.Y) >= 524288 || MathF.Abs(value.Z) >= 524288)
                    throw new InvalidDataException("Packed scene contains an invalid vertex.");
                vertices[i] = value;
            }

            bool collisionOnly = flags.HasFlag(MapPackedMeshFlags.CollisionOnly);
            bool solid = flags.HasFlag(MapPackedMeshFlags.Solid) || collisionOnly;
            Terrain terrain = terrainValue is >= byte.MinValue and <= byte.MaxValue
                && Enum.IsDefined(typeof(Terrain), (byte)terrainValue)
                    ? (Terrain)(byte)terrainValue : Terrain.Metal;
            int accepted = 0;
            for (int faceIndex = 0; faceIndex < faceCount; faceIndex++)
            {
                cancellation.ThrowIfCancellationRequested();
                int a = reader.ReadInt32(), b = reader.ReadInt32(), c = reader.ReadInt32();
                var uv = new[]
                {
                    new Vector2(reader.ReadSingle(), reader.ReadSingle()),
                    new Vector2(reader.ReadSingle(), reader.ReadSingle()),
                    new Vector2(reader.ReadSingle(), reader.ReadSingle())
                };
                if ((uint)a >= vertices.Length || (uint)b >= vertices.Length || (uint)c >= vertices.Length)
                    throw new InvalidDataException("Packed scene face references a missing vertex.");
                if (uv.Any(value => !float.IsFinite(value.X) || !float.IsFinite(value.Y)
                    || MathF.Abs(value.X) > 32760 || MathF.Abs(value.Y) > 32760))
                    throw new InvalidDataException("Packed scene contains an invalid texture coordinate.");
                Vector3[] points = { vertices[a], vertices[b], vertices[c] };
                Vector3 normal = Vector3.Cross(points[1] - points[0], points[2] - points[0]);
                if (!float.IsFinite(normal.LengthSquared) || normal.LengthSquared < 1e-10f) continue;
                normal.Normalize();
                var face = new BuiltFace(points, uv, normal, material + materialOffset, 1f)
                {
                    Terrain = terrain,
                    Damaging = flags.HasFlag(MapPackedMeshFlags.Damaging),
                    IgnorePlayers = flags.HasFlag(MapPackedMeshFlags.IgnorePlayers),
                    IgnoreBeams = flags.HasFlag(MapPackedMeshFlags.IgnoreBeams),
                    IgnoreScan = flags.HasFlag(MapPackedMeshFlags.IgnoreScan),
                    ReflectBeams = flags.HasFlag(MapPackedMeshFlags.ReflectBeams),
                    Sky = flags.HasFlag(MapPackedMeshFlags.Sky),
                    CollisionSource = collisionOnly ? "PackedSceneCollision" : "PackedSceneRender",
                    CollisionSourceId = meshIndex,
                    CollisionShader = label,
                    CollisionConfidence = collisionOnly ? .99f : 1f
                };
                if (!collisionOnly)
                {
                    map.Faces.Add(face);
                    renderFaces++;
                }
                if (solid)
                {
                    map.Solid.Add(face);
                    collisionFaces++;
                }
                accepted++;
            }
            if (accepted == 0)
                throw new InvalidDataException($"Packed scene mesh '{label}' contains no non-degenerate faces.");
            totalVertices += vertexCount;
            totalFaces += faceCount;
        }
        if (memory.Position != memory.Length)
            throw new InvalidDataException("Packed scene contains trailing or malformed data.");
        if (renderFaces == 0 || collisionFaces == 0)
            throw new InvalidDataException("Packed scene requires separate nonempty render and collision geometry.");
        map.ImportedFaceCount = renderFaces;
        map.ImportedCollisionFaceCount = collisionFaces;
        return new(meshCount, renderFaces, collisionFaces, totalVertices, bytes.LongLength);
    }
}
