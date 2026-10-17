using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MphRead.Mods.MapGen;

static class Phase7Check
{
    private static int _passed;

    public static int Main()
    {
        string temp = Path.Combine(Path.GetTempPath(), "project-prime-phase7-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            string bspPath = Path.Combine(temp, "phase7-fixture.bsp");
            File.WriteAllBytes(bspPath, BuildBsp(partialLightmap: false));
            Q3Bsp bsp = Q3Bsp.Load(bspPath, null);

            Check(bsp.Nodes.Count == 1, "BSP node parsed");
            Check(bsp.Leafs.Count == 1, "BSP leaf parsed");
            Check(bsp.LeafSurfaces.SequenceEqual(new[] { 0 }), "leaf-surface references parsed");
            Check(bsp.LeafBrushes.SequenceEqual(new[] { 0 }), "leaf-brush references parsed");
            Check(bsp.Fogs.Count == 1, "fog lump parsed");
            Check(bsp.Lightmaps.Count == 1 && bsp.Lightmaps[0].Rgb24.Length == Q3LightmapProcessing.RgbPageBytes,
                "128x128 RGB lightmap parsed");
            Check(bsp.LightGrid.Count == 1, "light grid parsed");
            Check(bsp.Visibility is { ClusterCount: 1, BytesPerCluster: 1 }, "PVS header parsed");
            Check(bsp.Visibility!.IsVisible(0, 0), "PVS visibility decoded");
            Check(Math.Abs(bsp.Vertices[1].Lightmap[0] - 1f) < 0.0001f,
                "secondary vertex UV retained");
            Check(bsp.Faces[0].Lightmap == 0
                && bsp.Faces[0].LightmapSize.SequenceEqual(new[] { 128, 128 }),
                "surface lightmap binding retained");

            byte[] overbrightSource = new byte[Q3LightmapProcessing.RgbPageBytes];
            overbrightSource[0] = 100;
            overbrightSource[1] = 50;
            overbrightSource[2] = 25;
            byte[] overbright = Q3LightmapProcessing.ApplyOverbright(overbrightSource, 2, 0);
            Check(overbright[0] == 255 && overbright[1] is 127 or 128 && overbright[2] is 63 or 64,
                "overbright normalization preserves hue");
            Check(Q3LightmapProcessing.ToRgba32(overbright).Length
                == Q3LightmapProcessing.Width * Q3LightmapProcessing.Height * 4,
                "RGB lightmap expands to RGBA once");

            UniversalNativeMapData fidelity = Q3NativeFidelity.Build(bsp, 35f, "phase7-fixture");
            Check(fidelity.Validate().Count == 0, "Universal native-fidelity metadata validates");
            Check(fidelity.Lightmaps.Count == 1 && fidelity.SurfaceLighting.Count == 1,
                "lightmap metadata translated");
            Check(fidelity.Visibility?.Cells.Count == 1, "visibility cells translated");
            Check(fidelity.MaterialStacks.Count == 1
                && fidelity.MaterialStacks[0].Layers.Any(layer => layer.IsLightmap),
                "layered material contains lightmap stage");

            string projectPath = Path.Combine(temp, "phase7.json");
            var definition = new MapDefinition
            {
                Name = "PHASE7_TEST",
                NativeFidelity = fidelity
            };
            var mesh = new MapMesh { Label = "multi UV fixture" };
            mesh.Vertices.AddRange(new[]
            {
                new[] { 0f, 0f, 0f }, new[] { 1f, 0f, 0f }, new[] { 0f, 0f, 1f }
            });
            mesh.Faces.Add(new[] { 0, 1, 2 });
            mesh.FaceTexcoords.Add(new[]
            {
                new[] { 0f, 0f }, new[] { 1f, 0f }, new[] { 0f, 1f }
            });
            mesh.AdditionalUvChannels.Add(new MapMeshUvChannel
            {
                Channel = 1,
                Name = "Lightmap",
                FaceTexcoords = new List<float[][]?>
                {
                    new[] { new[] { 0f, 0f }, new[] { 1f, 0f }, new[] { 0f, 1f } }
                }
            });
            definition.Geometry.Add(mesh);
            definition.Save(projectPath);
            MapDefinition loaded = MapDefinition.Load(projectPath);
            MapMesh loadedMesh = (MapMesh)loaded.Geometry.Single();
            Check(loaded.NativeFidelity?.Lightmaps.Count == 1, "native metadata JSON round-trip");
            Check(loadedMesh.AdditionalUvChannels.Count == 1
                && loadedMesh.AdditionalUvChannels[0].FaceTexcoords.Count == 1,
                "secondary UV JSON round-trip");

            string badPath = Path.Combine(temp, "phase7-bad-lightmap.bsp");
            File.WriteAllBytes(badPath, BuildBsp(partialLightmap: true));
            bool rejected = false;
            try { _ = Q3Bsp.Load(badPath, null); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected, "partial lightmap page rejected safely");

            Console.WriteLine($"Phase 7 foundation checks passed: {_passed}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); } catch { }
        }
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("FAILED: " + name);
        _passed++;
        Console.WriteLine("PASS: " + name);
    }

    private static byte[] BuildBsp(bool partialLightmap)
    {
        var lumps = new byte[17][];
        lumps[0] = Encoding.ASCII.GetBytes("{\n\"classname\" \"worldspawn\"\n}\n");
        lumps[1] = Record(writer =>
        {
            FixedString(writer, "textures/phase7/base", 64);
            writer.Write(0);
            writer.Write(Q3Bsp.ContentsSolid);
        });
        lumps[2] = Record(writer =>
        {
            writer.Write(0f); writer.Write(1f); writer.Write(0f); writer.Write(0f);
        });
        lumps[3] = Record(writer =>
        {
            writer.Write(0); writer.Write(-1); writer.Write(-1);
            Int3(writer, -64, -64, -64); Int3(writer, 64, 64, 64);
        });
        lumps[4] = Record(writer =>
        {
            writer.Write(0); writer.Write(0);
            Int3(writer, -64, -64, -64); Int3(writer, 64, 64, 64);
            writer.Write(0); writer.Write(1); writer.Write(0); writer.Write(1);
        });
        lumps[5] = Record(writer => writer.Write(0));
        lumps[6] = Record(writer => writer.Write(0));
        lumps[7] = Record(writer =>
        {
            Float3(writer, -64, -64, -64); Float3(writer, 64, 64, 64);
            writer.Write(0); writer.Write(1); writer.Write(0); writer.Write(1);
        });
        lumps[8] = Record(writer => { writer.Write(0); writer.Write(1); writer.Write(0); });
        lumps[9] = Record(writer => { writer.Write(0); writer.Write(0); });
        lumps[10] = Combine(
            Vertex(0, 0, 0, 0, 0, 0, 0),
            Vertex(64, 0, 0, 1, 0, 1, 0),
            Vertex(0, 64, 0, 0, 1, 0, 1));
        lumps[11] = Record(writer => { writer.Write(0); writer.Write(1); writer.Write(2); });
        lumps[12] = Record(writer =>
        {
            FixedString(writer, "textures/phase7/fog", 64);
            writer.Write(-1); writer.Write(-1);
        });
        lumps[13] = Record(writer =>
        {
            writer.Write(0); writer.Write(-1); writer.Write(1);
            writer.Write(0); writer.Write(3); writer.Write(0); writer.Write(3);
            writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(128); writer.Write(128);
            Float3(writer, 0, 0, 0);
            Float3(writer, 1, 0, 0); Float3(writer, 0, 1, 0);
            Float3(writer, 0, 0, 1);
            writer.Write(0); writer.Write(0);
        });
        lumps[14] = new byte[Q3LightmapProcessing.RgbPageBytes - (partialLightmap ? 1 : 0)];
        for (int i = 0; i + 2 < lumps[14].Length; i += 3)
        {
            lumps[14][i] = 96;
            lumps[14][i + 1] = 48;
            lumps[14][i + 2] = 24;
        }
        lumps[15] = new byte[] { 8, 8, 8, 16, 16, 16, 0, 0 };
        lumps[16] = Record(writer => { writer.Write(1); writer.Write(1); writer.Write((byte)1); });

        const int headerSize = 8 + 17 * 8;
        int offset = headerSize;
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write(Encoding.ASCII.GetBytes("IBSP"));
        writer.Write(46);
        foreach (byte[] lump in lumps)
        {
            writer.Write(offset);
            writer.Write(lump.Length);
            offset += lump.Length;
        }
        foreach (byte[] lump in lumps) writer.Write(lump);
        return stream.ToArray();
    }

    private static byte[] Vertex(float x, float y, float z, float s, float t, float ls, float lt)
        => Record(writer =>
        {
            Float3(writer, x, y, z);
            writer.Write(s); writer.Write(t); writer.Write(ls); writer.Write(lt);
            Float3(writer, 0, 0, 1);
            writer.Write(new byte[] { 128, 96, 64, 255 });
        });

    private static byte[] Record(Action<BinaryWriter> write)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        write(writer);
        writer.Flush();
        return stream.ToArray();
    }

    private static byte[] Combine(params byte[][] values)
    {
        using var stream = new MemoryStream();
        foreach (byte[] value in values) stream.Write(value);
        return stream.ToArray();
    }

    private static void FixedString(BinaryWriter writer, string value, int size)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(value);
        writer.Write(bytes, 0, Math.Min(bytes.Length, size));
        for (int i = Math.Min(bytes.Length, size); i < size; i++) writer.Write((byte)0);
    }

    private static void Int3(BinaryWriter writer, int x, int y, int z)
    {
        writer.Write(x); writer.Write(y); writer.Write(z);
    }

    private static void Float3(BinaryWriter writer, float x, float y, float z)
    {
        writer.Write(x); writer.Write(y); writer.Write(z);
    }
}
