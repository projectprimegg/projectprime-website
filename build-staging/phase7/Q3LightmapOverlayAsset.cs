using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenTK.Mathematics;

namespace MphRead.Mods.MapGen;

/// <summary>
/// Render-only geometry carrying UV1 for the source baked-lighting pass.
/// Positions are quantized through the same native fixed-point conversion as
/// MapPacker, so a depth-equal overlay covers only the source surface that
/// produced the scene color.
/// </summary>
public static class Q3LightmapOverlayAsset
{
    private static readonly byte[] Magic =
        { (byte)'P', (byte)'P', (byte)'L', (byte)'M', (byte)'E', (byte)'S', (byte)'H', 1 };

    public const int MaxGroups = 4096;
    public const int MaxVertices = 12_000_000;
    private const int HeaderBytes = 16;
    private const int GroupHeaderBytes = 8;
    private const int VertexBytes = 20;

    public readonly record struct Vertex(Vector3 Position, Vector2 LightmapUv);
    public sealed record Group(int Page, IReadOnlyList<Vertex> Vertices);
    public sealed record Overlay(IReadOnlyList<Group> Groups, int VertexCount);

    public static Overlay Build(BuiltMap map, int scaleFactor)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (scaleFactor is < 0 or > 10)
            throw new ArgumentOutOfRangeException(nameof(scaleFactor));
        float scale = MathF.Pow(2, scaleFactor);
        var groups = new SortedDictionary<int, List<Vertex>>();
        int total = 0;

        foreach (BuiltFace face in map.Faces)
        {
            if (face.LightmapPage < 0 || face.Points.Length < 3
                || !face.AdditionalTexcoords.TryGetValue(1, out Vector2[]? uv)
                || uv.Length != face.Points.Length)
                continue;
            if (!groups.TryGetValue(face.LightmapPage, out List<Vertex>? vertices))
            {
                if (groups.Count >= MaxGroups)
                    throw new InvalidDataException("Lightmap overlay has too many pages.");
                vertices = new List<Vertex>();
                groups.Add(face.LightmapPage, vertices);
            }
            for (int corner = 1; corner + 1 < face.Points.Length; corner++)
            {
                Add(0);
                Add(corner);
                Add(corner + 1);
            }

            void Add(int index)
            {
                if (++total > MaxVertices)
                    throw new InvalidDataException("Lightmap overlay has too many vertices.");
                Vector3 point = Quantize(face.Points[index], scale);
                Vector2 texcoord = uv[index];
                if (!Finite(point) || !Single.IsFinite(texcoord.X) || !Single.IsFinite(texcoord.Y))
                    throw new InvalidDataException("Lightmap overlay contains non-finite data.");
                vertices.Add(new Vertex(point, texcoord));
            }
        }

        return new Overlay(groups.Select(pair => new Group(pair.Key, pair.Value.ToArray())).ToArray(), total);
    }

    public static byte[] Encode(Overlay overlay)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        if (overlay.Groups.Count > MaxGroups || overlay.VertexCount > MaxVertices)
            throw new InvalidDataException("Lightmap overlay exceeds format limits.");
        long length = HeaderBytes + overlay.Groups.Count * (long)GroupHeaderBytes
            + overlay.VertexCount * (long)VertexBytes;
        if (length > MapPackageReader.MaxEntryBytes || length > Int32.MaxValue)
            throw new InvalidDataException("Lightmap overlay asset is too large.");

        using var stream = new MemoryStream((int)length);
        using var writer = new BinaryWriter(stream);
        writer.Write(Magic);
        writer.Write(overlay.Groups.Count);
        writer.Write(overlay.VertexCount);
        int counted = 0;
        foreach (Group group in overlay.Groups)
        {
            if (group.Page < 0 || group.Vertices.Count % 3 != 0)
                throw new InvalidDataException("Invalid lightmap overlay group.");
            writer.Write(group.Page);
            writer.Write(group.Vertices.Count);
            foreach (Vertex vertex in group.Vertices)
            {
                if (!Finite(vertex.Position)
                    || !Single.IsFinite(vertex.LightmapUv.X)
                    || !Single.IsFinite(vertex.LightmapUv.Y))
                    throw new InvalidDataException("Lightmap overlay contains non-finite data.");
                writer.Write(vertex.Position.X);
                writer.Write(vertex.Position.Y);
                writer.Write(vertex.Position.Z);
                writer.Write(vertex.LightmapUv.X);
                writer.Write(vertex.LightmapUv.Y);
                counted++;
            }
        }
        if (counted != overlay.VertexCount || stream.Length != length)
            throw new InvalidDataException("Lightmap overlay count changed while writing.");
        return stream.ToArray();
    }

    public static Overlay Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < HeaderBytes || !bytes[..Magic.Length].SequenceEqual(Magic))
            throw new InvalidDataException("Invalid lightmap overlay header.");
        int groupCount = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(8, 4));
        int vertexCount = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(12, 4));
        if (groupCount is < 0 or > MaxGroups || vertexCount is < 0 or > MaxVertices)
            throw new InvalidDataException("Invalid lightmap overlay dimensions.");
        long expected = HeaderBytes + groupCount * (long)GroupHeaderBytes
            + vertexCount * (long)VertexBytes;
        if (expected != bytes.Length)
            throw new InvalidDataException("Lightmap overlay length does not match its header.");

        int offset = HeaderBytes;
        int counted = 0;
        var groups = new List<Group>(groupCount);
        var seenPages = new HashSet<int>();
        for (int groupIndex = 0; groupIndex < groupCount; groupIndex++)
        {
            int page = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(offset, 4));
            int count = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(offset + 4, 4));
            offset += GroupHeaderBytes;
            if (page < 0 || !seenPages.Add(page) || count < 0 || count % 3 != 0
                || count > MaxVertices - counted)
                throw new InvalidDataException("Invalid lightmap overlay group.");
            var vertices = new Vertex[count];
            for (int i = 0; i < count; i++)
            {
                float x = ReadSingle(bytes, ref offset);
                float y = ReadSingle(bytes, ref offset);
                float z = ReadSingle(bytes, ref offset);
                float u = ReadSingle(bytes, ref offset);
                float v = ReadSingle(bytes, ref offset);
                var vertex = new Vertex(new Vector3(x, y, z), new Vector2(u, v));
                if (!Finite(vertex.Position) || !Single.IsFinite(u) || !Single.IsFinite(v))
                    throw new InvalidDataException("Lightmap overlay contains non-finite data.");
                vertices[i] = vertex;
            }
            counted += count;
            groups.Add(new Group(page, vertices));
        }
        if (counted != vertexCount || offset != bytes.Length)
            throw new InvalidDataException("Lightmap overlay ended at the wrong offset.");
        return new Overlay(groups, vertexCount);
    }

    private static float ReadSingle(ReadOnlySpan<byte> bytes, ref int offset)
    {
        float value = BitConverter.Int32BitsToSingle(
            BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(offset, 4)));
        offset += 4;
        return value;
    }

    private static Vector3 Quantize(Vector3 point, float scale)
        => new(Quantize(point.X, scale), Quantize(point.Y, scale), Quantize(point.Z, scale));

    private static float Quantize(float value, float scale)
    {
        int packed = Fixed.ToInt(value / scale);
        if (packed is < Int16.MinValue or > Int16.MaxValue)
            throw new InvalidDataException("Lightmap overlay vertex exceeds native fixed-point range.");
        return Fixed.ToFloat(packed) * scale;
    }

    private static bool Finite(Vector3 value)
        => Single.IsFinite(value.X) && Single.IsFinite(value.Y) && Single.IsFinite(value.Z);
}
