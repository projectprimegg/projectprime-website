using System;

namespace MphRead.Mods.MapGen;

/// <summary>Quake 3 lightmap color conversion without renderer-side file I/O.</summary>
public static class Q3LightmapProcessing
{
    public const int Width = 128;
    public const int Height = 128;
    public const int RgbPageBytes = Width * Height * 3;

    /// <summary>
    /// Applies Quake's overbright shift and hue-preserving normalization.
    /// The returned RGB page is a new array suitable for caching or packaging.
    /// </summary>
    public static byte[] ApplyOverbright(ReadOnlySpan<byte> rgb24,
        int mapOverbrightBits = 2, int rendererOverbrightBits = 0, float exposure = 1f)
    {
        if (rgb24.Length != RgbPageBytes)
            throw new ArgumentException($"A Quake lightmap must contain {RgbPageBytes} RGB bytes.", nameof(rgb24));
        if (mapOverbrightBits is < 0 or > 4 || rendererOverbrightBits is < 0 or > 4)
            throw new ArgumentOutOfRangeException(nameof(mapOverbrightBits));
        if (!Single.IsFinite(exposure) || exposure is < 0.01f or > 32f)
            throw new ArgumentOutOfRangeException(nameof(exposure));

        float scale = MathF.Pow(2, mapOverbrightBits - rendererOverbrightBits) * exposure;
        var output = new byte[rgb24.Length];
        for (int i = 0; i < rgb24.Length; i += 3)
        {
            float r = rgb24[i] * scale;
            float g = rgb24[i + 1] * scale;
            float b = rgb24[i + 2] * scale;
            float maximum = MathF.Max(r, MathF.Max(g, b));
            if (maximum > 255f)
            {
                float normalize = 255f / maximum;
                r *= normalize;
                g *= normalize;
                b *= normalize;
            }
            output[i] = ClampByte(r);
            output[i + 1] = ClampByte(g);
            output[i + 2] = ClampByte(b);
        }
        return output;
    }

    public static byte[] ToRgba32(ReadOnlySpan<byte> rgb24)
    {
        if (rgb24.Length % 3 != 0)
            throw new ArgumentException("RGB input has a partial pixel.", nameof(rgb24));
        var rgba = new byte[rgb24.Length / 3 * 4];
        for (int source = 0, target = 0; source < rgb24.Length; source += 3, target += 4)
        {
            rgba[target] = rgb24[source];
            rgba[target + 1] = rgb24[source + 1];
            rgba[target + 2] = rgb24[source + 2];
            rgba[target + 3] = 255;
        }
        return rgba;
    }

    private static byte ClampByte(float value)
        => (byte)Math.Clamp((int)MathF.Round(value), 0, 255);
}
