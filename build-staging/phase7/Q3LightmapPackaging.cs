using System;
using System.Buffers.Binary;
using System.IO;
using ReFuel.Stb;

namespace MphRead.Mods.MapGen;

/// <summary>Portable package encoding for source baked-lighting data.</summary>
public static class Q3LightmapPackaging
{
    public static byte[] EncodePng(Q3LightmapPage page, int overbrightBits = 2,
        int rendererOverbrightBits = 0, float exposure = 1f)
    {
        if (page.Width != Q3LightmapProcessing.Width
            || page.Height != Q3LightmapProcessing.Height)
            throw new InvalidDataException("Quake lightmap dimensions must be 128x128.");
        byte[] processed = Q3LightmapProcessing.ApplyOverbright(page.Rgb24,
            overbrightBits, rendererOverbrightBits, exposure);
        using var stream = new MemoryStream();
        Mods.PngEncoder.Write<byte>(processed, page.Width, page.Height,
            StbiImageFormat.Rgb, stream);
        return stream.ToArray();
    }
}

/// <summary>
/// Small self-describing PVS sidecar. The source BSP remains the authority;
/// this asset avoids reparsing it every time the renderer needs cluster rows.
/// </summary>
public static class Q3VisibilityAsset
{
    private static readonly byte[] Magic = { (byte)'P', (byte)'P', (byte)'V', (byte)'S', 1, 0, 0, 0 };

    public static byte[] Encode(Q3Visibility visibility)
    {
        ArgumentNullException.ThrowIfNull(visibility);
        int expected = checked(visibility.ClusterCount * visibility.BytesPerCluster);
        if (visibility.ClusterCount is < 0 or > 65536
            || visibility.BytesPerCluster is < 0 or > 8192
            || visibility.Data.Length != expected)
            throw new InvalidDataException("Invalid Quake PVS dimensions.");
        byte[] output = new byte[checked(16 + expected)];
        Magic.CopyTo(output, 0);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(8, 4), visibility.ClusterCount);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(12, 4), visibility.BytesPerCluster);
        visibility.Data.CopyTo(output, 16);
        return output;
    }

    public static Q3Visibility Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 16 || !bytes[..8].SequenceEqual(Magic))
            throw new InvalidDataException("Invalid Project Prime PVS asset header.");
        int clusters = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(8, 4));
        int row = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(12, 4));
        if (clusters is < 0 or > 65536 || row is < 0 or > 8192
            || (long)clusters * row != bytes.Length - 16)
            throw new InvalidDataException("Invalid Project Prime PVS asset dimensions.");
        return new Q3Visibility(clusters, row, bytes[16..].ToArray());
    }
}
