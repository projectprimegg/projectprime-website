using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace MphRead.Mods.MapGen;

/// <summary>
/// Compact, project-embedded triangle mesh for very large imported maps.
/// Vertices use the runtime's exact 20.12 fixed-point coordinates, UVs use
/// signed 1.11.4 texel coordinates, and the payload is ZLib-compressed before
/// base64 embedding. The decoded chunks remain under the legacy 65,535 mesh
/// limits and are compiled through the ordinary MapMesh path.
/// </summary>
public static class MapPackedMeshCodec
{
    public const string EncodingName = "zlib-base64-v1";
    public const int MaxChunks = 4096;
    public const int MaxVerticesPerChunk = 60000;
    public const int MaxFacesPerChunk = 60000;
    public const int MaxTotalFaces = 2_000_000;
    public const int MaxCompressedBytes = 64 * 1024 * 1024;
    public const int MaxExpandedBytes = 256 * 1024 * 1024;
    private const float CoordinateScale = 4096f;
    private const float UvScale = 16f;

    public sealed record Statistics(int Chunks, int Vertices, int Faces,
        int CompressedBytes, int ExpandedBytes, int MaximumMaterial);

    public static IReadOnlyList<BuiltFace> Compile(
        MapPackedMesh source, float texScale, int materialOffset = 0)
    {
        var result = new List<BuiltFace>();
        foreach (MapMesh chunk in Decode(source))
            result.AddRange(GeometryCompiler.Compile(chunk, texScale, materialOffset));
        return result;
    }

    public static IReadOnlyList<MapMesh> Decode(MapPackedMesh source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!String.Equals(source.Encoding, EncodingName, StringComparison.Ordinal))
            throw new MapAuthoringException("FP-MESH-002",
                $"Unsupported packed mesh encoding '{source.Encoding}'. Expected {EncodingName}.");
        if (String.IsNullOrWhiteSpace(source.Data))
            throw new MapAuthoringException("FP-MESH-002", "Packed mesh data is empty.");

        byte[] compressed;
        try { compressed = Convert.FromBase64String(source.Data); }
        catch (FormatException ex)
        { throw new MapAuthoringException("FP-MESH-002", "Packed mesh data is not valid base64: " + ex.Message); }
        if (compressed.Length is < 8 or > MaxCompressedBytes)
            throw new MapAuthoringException("FP-MESH-002", "Packed mesh compressed size is outside the supported range.");

        byte[] expanded = Expand(compressed);
        using var stream = new MemoryStream(expanded, writable: false);
        using var reader = new BinaryReader(stream);
        byte[] magic = ReadExact(reader, 4);
        if (!magic.AsSpan().SequenceEqual("PPM1"u8))
            throw new MapAuthoringException("FP-MESH-002", "Packed mesh signature is invalid.");
        int chunkCount = reader.ReadInt32();
        if (chunkCount is < 1 or > MaxChunks)
            throw new MapAuthoringException("FP-MESH-002", "Packed mesh chunk count is outside the supported range.");

        int totalFaces = 0;
        var chunks = new List<MapMesh>(chunkCount);
        for (int chunkIndex = 0; chunkIndex < chunkCount; chunkIndex++)
        {
            int vertexCount = reader.ReadInt32();
            int faceCount = reader.ReadInt32();
            if (vertexCount is < 3 or > MaxVerticesPerChunk
                || faceCount is < 1 or > MaxFacesPerChunk
                || (totalFaces += faceCount) > MaxTotalFaces)
                throw new MapAuthoringException("FP-MESH-002",
                    $"Packed mesh chunk {chunkIndex + 1} exceeds the bounded vertex/face limits.");

            var vertices = new List<float[]>(vertexCount);
            for (int vertex = 0; vertex < vertexCount; vertex++)
            {
                vertices.Add(new[]
                {
                    reader.ReadInt32() / CoordinateScale,
                    reader.ReadInt32() / CoordinateScale,
                    reader.ReadInt32() / CoordinateScale
                });
            }

            var faces = new List<int[]>(faceCount);
            var materials = new List<int>(faceCount);
            var texcoords = new List<float[][]?>(faceCount);
            for (int face = 0; face < faceCount; face++)
            {
                int a = reader.ReadUInt16(), b = reader.ReadUInt16(), c = reader.ReadUInt16();
                int material = reader.ReadUInt16();
                if (a >= vertexCount || b >= vertexCount || c >= vertexCount
                    || a == b || b == c || a == c)
                    throw new MapAuthoringException("FP-MESH-002",
                        $"Packed mesh chunk {chunkIndex + 1} has an invalid triangle index.");
                faces.Add(new[] { a, b, c });
                materials.Add(material);
                texcoords.Add(new[]
                {
                    new[] { reader.ReadInt16() / UvScale, reader.ReadInt16() / UvScale },
                    new[] { reader.ReadInt16() / UvScale, reader.ReadInt16() / UvScale },
                    new[] { reader.ReadInt16() / UvScale, reader.ReadInt16() / UvScale }
                });
            }

            chunks.Add(new MapMesh
            {
                Id = source.Id,
                Label = chunkCount == 1 ? source.Label : $"{source.Label} [{chunkIndex + 1}/{chunkCount}]",
                Transform = Copy(source.Transform),
                Material = source.Material,
                Solid = source.Solid,
                Damaging = source.Damaging,
                Terrain = source.Terrain,
                Shade = source.Shade,
                Uv = Copy(source.Uv),
                FaceUv = new(),
                Hidden = source.Hidden,
                Locked = source.Locked,
                Layer = source.Layer,
                CollisionOnly = source.CollisionOnly,
                Slipperiness = source.Slipperiness,
                ReflectBeams = source.ReflectBeams,
                IgnorePlayers = source.IgnorePlayers,
                IgnoreBeams = source.IgnoreBeams,
                IgnoreScan = source.IgnoreScan,
                Vertices = vertices,
                Faces = faces,
                FaceMaterials = materials,
                FaceTexcoords = texcoords
            });
        }
        if (stream.Position != stream.Length)
            throw new MapAuthoringException("FP-MESH-002", "Packed mesh contains unexpected trailing data.");
        return chunks;
    }

    /// <summary>
    /// Public encoder used by importers and conversion tools. Each supplied mesh
    /// becomes one bounded packed chunk; callers split larger sources before
    /// invoking this method.
    /// </summary>
    public static string Encode(IReadOnlyList<MapMesh> chunks)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        if (chunks.Count is < 1 or > MaxChunks)
            throw new MapAuthoringException("FP-MESH-002", "Packed mesh requires 1-4096 chunks.");
        using var expanded = new MemoryStream();
        using (var writer = new BinaryWriter(expanded, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write("PPM1"u8);
            writer.Write(chunks.Count);
            int totalFaces = 0;
            foreach (MapMesh chunk in chunks)
            {
                if (chunk.Vertices == null || chunk.Faces == null
                    || chunk.Vertices.Count is < 3 or > MaxVerticesPerChunk
                    || chunk.Faces.Count is < 1 or > MaxFacesPerChunk
                    || (totalFaces += chunk.Faces.Count) > MaxTotalFaces)
                    throw new MapAuthoringException("FP-MESH-002", "Mesh chunk exceeds packed geometry limits.");
                writer.Write(chunk.Vertices.Count);
                writer.Write(chunk.Faces.Count);
                foreach (float[] vertex in chunk.Vertices)
                {
                    if (!MapValidator.Vector(vertex))
                        throw new MapAuthoringException("FP-MESH-002", "Packed mesh vertex is not a finite triple.");
                    foreach (float value in vertex)
                    {
                        double fixedValue = Math.Round(value * CoordinateScale);
                        if (!Double.IsFinite(fixedValue) || fixedValue < Int32.MinValue || fixedValue > Int32.MaxValue)
                            throw new MapAuthoringException("FP-MESH-002", "Packed mesh coordinate exceeds fixed-point range.");
                        writer.Write((int)fixedValue);
                    }
                }
                for (int faceIndex = 0; faceIndex < chunk.Faces.Count; faceIndex++)
                {
                    int[] face = chunk.Faces[faceIndex];
                    if (face == null || face.Length != 3
                        || face.Any(index => index < 0 || index >= chunk.Vertices.Count))
                        throw new MapAuthoringException("FP-MESH-002", "Packed geometry accepts triangles only.");
                    writer.Write((ushort)face[0]); writer.Write((ushort)face[1]); writer.Write((ushort)face[2]);
                    int material = chunk.FaceMaterials != null && faceIndex < chunk.FaceMaterials.Count
                        ? chunk.FaceMaterials[faceIndex] : chunk.Material;
                    if (material is < 0 or > UInt16.MaxValue)
                        throw new MapAuthoringException("FP-MESH-002", "Packed mesh material index exceeds UInt16 range.");
                    writer.Write((ushort)material);
                    float[][]? uv = chunk.FaceTexcoords != null && faceIndex < chunk.FaceTexcoords.Count
                        ? chunk.FaceTexcoords[faceIndex] : null;
                    if (uv == null || uv.Length != 3 || uv.Any(value => value == null || value.Length != 2))
                        uv = new[] { new float[2], new float[2], new float[2] };
                    foreach (float[] corner in uv)
                    {
                        foreach (float value in corner)
                        {
                            double fixedValue = Math.Round(value * UvScale);
                            if (!Double.IsFinite(fixedValue) || fixedValue < Int16.MinValue || fixedValue > Int16.MaxValue)
                                throw new MapAuthoringException("FP-MESH-002", "Packed mesh UV exceeds signed 1.11.4 range.");
                            writer.Write((short)fixedValue);
                        }
                    }
                }
            }
        }
        if (expanded.Length > MaxExpandedBytes)
            throw new MapAuthoringException("FP-MESH-002", "Packed mesh expanded payload is too large.");
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
            expanded.WriteTo(zlib);
        if (compressed.Length > MaxCompressedBytes)
            throw new MapAuthoringException("FP-MESH-002", "Packed mesh compressed payload is too large.");
        return Convert.ToBase64String(compressed.ToArray());
    }

    public static Statistics Inspect(MapPackedMesh source)
    {
        IReadOnlyList<MapMesh> chunks = Decode(source);
        int maximumMaterial = chunks.SelectMany(chunk => chunk.FaceMaterials).DefaultIfEmpty(source.Material).Max();
        int compressed = Convert.FromBase64String(source.Data).Length;
        return new(chunks.Count, chunks.Sum(chunk => chunk.Vertices.Count),
            chunks.Sum(chunk => chunk.Faces.Count), compressed, 0, maximumMaterial);
    }

    private static byte[] Expand(byte[] compressed)
    {
        using var input = new MemoryStream(compressed, writable: false);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        byte[] buffer = new byte[65536];
        int count;
        while ((count = zlib.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (output.Length + count > MaxExpandedBytes)
                throw new MapAuthoringException("FP-MESH-002", "Packed mesh expands beyond the safety limit.");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }

    private static byte[] ReadExact(BinaryReader reader, int count)
    {
        byte[] bytes = reader.ReadBytes(count);
        if (bytes.Length != count)
            throw new MapAuthoringException("FP-MESH-002", "Packed mesh payload is truncated.");
        return bytes;
    }

    private static MapTransform Copy(MapTransform? value)
        => value == null ? new() : new()
        {
            Position = (float[])value.Position.Clone(),
            Rotation = (float[])value.Rotation.Clone(),
            Scale = (float[])value.Scale.Clone()
        };

    private static MapUv Copy(MapUv? value)
        => value == null ? new() : new()
        {
            Scale = (float[])value.Scale.Clone(),
            Offset = (float[])value.Offset.Clone(),
            Rotation = value.Rotation
        };
}
