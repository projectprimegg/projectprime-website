from pathlib import Path
import sys


def replace_once(path: Path, before: str, after: str, label: str) -> None:
    text = path.read_text(encoding='utf-8')
    count = text.count(before)
    if count != 1:
        raise RuntimeError(f'{label}: expected one anchor, found {count}')
    path.write_text(text.replace(before, after, 1), encoding='utf-8', newline='\n')


if len(sys.argv) != 2:
    raise SystemExit('usage: apply_q3_bsp_phase7.py <project-root>')
root = Path(sys.argv[1]).resolve()
path = root / 'src/MphRead/Mods/MapGen/Q3Bsp.cs'

replace_once(path,
'''    public class Q3Bsp
    {
''',
'''    public partial class Q3Bsp
    {
''', 'partial Q3Bsp')

replace_once(path,
'''        public IReadOnlyList<Q3Model> Models { get; private set; } = Array.Empty<Q3Model>();
        public IReadOnlyList<Dictionary<string, string>> Entities { get; private set; }
''',
'''        public IReadOnlyList<Q3Model> Models { get; private set; } = Array.Empty<Q3Model>();
        public IReadOnlyList<Q3Node> Nodes { get; private set; } = Array.Empty<Q3Node>();
        public IReadOnlyList<Q3Leaf> Leafs { get; private set; } = Array.Empty<Q3Leaf>();
        public IReadOnlyList<int> LeafSurfaces { get; private set; } = Array.Empty<int>();
        public IReadOnlyList<int> LeafBrushes { get; private set; } = Array.Empty<int>();
        public IReadOnlyList<Q3Fog> Fogs { get; private set; } = Array.Empty<Q3Fog>();
        public IReadOnlyList<Q3LightmapPage> Lightmaps { get; private set; } = Array.Empty<Q3LightmapPage>();
        public IReadOnlyList<Q3LightGridSample> LightGrid { get; private set; } = Array.Empty<Q3LightGridSample>();
        public Q3Visibility? Visibility { get; private set; }
        public IReadOnlyList<Dictionary<string, string>> Entities { get; private set; }
''', 'Q3 fidelity properties')

replace_once(path,
'''        public static readonly int[] UsedLumps = { 0, 1, 2, 7, 8, 9, 10, 11, 13 };
''',
'''        public static readonly int[] UsedLumps = Enumerable.Range(0, 17).ToArray();
''', 'complete BSP lump preservation')

replace_once(path,
'''            bsp.Planes = Lump(reader, offsets[2], 16, r =>
                new Q3Plane(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle()));
            bsp.Models = Lump(reader, offsets[7], 40, r =>
''',
'''            bsp.Planes = Lump(reader, offsets[2], 16, r =>
                new Q3Plane(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle()));
            bsp.Nodes = Lump(reader, offsets[3], 36, r =>
                new Q3Node(r.ReadInt32(), new[] { r.ReadInt32(), r.ReadInt32() },
                    new[] { r.ReadInt32(), r.ReadInt32(), r.ReadInt32() },
                    new[] { r.ReadInt32(), r.ReadInt32(), r.ReadInt32() }));
            bsp.Leafs = Lump(reader, offsets[4], 48, r =>
                new Q3Leaf(r.ReadInt32(), r.ReadInt32(),
                    new[] { r.ReadInt32(), r.ReadInt32(), r.ReadInt32() },
                    new[] { r.ReadInt32(), r.ReadInt32(), r.ReadInt32() },
                    r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32()));
            bsp.LeafSurfaces = Lump(reader, offsets[5], 4, r => r.ReadInt32());
            bsp.LeafBrushes = Lump(reader, offsets[6], 4, r => r.ReadInt32());
            bsp.Models = Lump(reader, offsets[7], 40, r =>
''', 'nodes leaves and references')

replace_once(path,
'''                float[] surface = new[] { r.ReadSingle(), r.ReadSingle() };
                r.ReadSingle(); // lightmap S
                r.ReadSingle(); // lightmap T
                float[] normal = new[] { r.ReadSingle(), r.ReadSingle(), r.ReadSingle() };
                byte[] color = r.ReadBytes(4);
                return new Q3Vertex(position, surface, normal, color);
''',
'''                float[] surface = new[] { r.ReadSingle(), r.ReadSingle() };
                float[] lightmap = new[] { r.ReadSingle(), r.ReadSingle() };
                float[] normal = new[] { r.ReadSingle(), r.ReadSingle(), r.ReadSingle() };
                byte[] color = r.ReadBytes(4);
                return new Q3Vertex(position, surface, lightmap, normal, color);
''', 'vertex lightmap coordinates')

replace_once(path,
'''            bsp.MeshVerts = Lump(reader, offsets[11], 4, r => r.ReadInt32());
            bsp.Faces = Lump(reader, offsets[13], 104, r =>
''',
'''            bsp.MeshVerts = Lump(reader, offsets[11], 4, r => r.ReadInt32());
            bsp.Fogs = Lump(reader, offsets[12], 72, r =>
            {
                string name = Encoding.ASCII.GetString(r.ReadBytes(64)).TrimEnd('\\0');
                return new Q3Fog(name, r.ReadInt32(), r.ReadInt32());
            });
            bsp.Faces = Lump(reader, offsets[13], 104, r =>
''', 'fog records')

replace_once(path,
'''                int meshVert = r.ReadInt32();
                int meshVertCount = r.ReadInt32();
                r.ReadInt32(); // lightmap index
                r.ReadBytes(8 + 8 + 12 + 24); // lightmap start, size, origin, vectors
                float[] normal = new[] { r.ReadSingle(), r.ReadSingle(), r.ReadSingle() };
                int[] size = new[] { r.ReadInt32(), r.ReadInt32() };
                return new Q3Face(texture, effect, type, vertex, vertexCount, meshVert, meshVertCount, normal, size);
            });
''',
'''                int meshVert = r.ReadInt32();
                int meshVertCount = r.ReadInt32();
                int lightmap = r.ReadInt32();
                int[] lightmapStart = new[] { r.ReadInt32(), r.ReadInt32() };
                int[] lightmapSize = new[] { r.ReadInt32(), r.ReadInt32() };
                float[] lightmapOrigin = new[] { r.ReadSingle(), r.ReadSingle(), r.ReadSingle() };
                float[][] lightmapVectors = new[]
                {
                    new[] { r.ReadSingle(), r.ReadSingle(), r.ReadSingle() },
                    new[] { r.ReadSingle(), r.ReadSingle(), r.ReadSingle() }
                };
                float[] normal = new[] { r.ReadSingle(), r.ReadSingle(), r.ReadSingle() };
                int[] size = new[] { r.ReadInt32(), r.ReadInt32() };
                return new Q3Face(texture, effect, type, vertex, vertexCount,
                    meshVert, meshVertCount, lightmap, lightmapStart, lightmapSize,
                    lightmapOrigin, lightmapVectors, normal, size);
            });
            bsp.Lightmaps = ReadLightmaps(bytes, offsets[14], cancellation);
            bsp.LightGrid = Lump(reader, offsets[15], 8, r =>
                new Q3LightGridSample(r.ReadBytes(3), r.ReadBytes(3), r.ReadByte(), r.ReadByte()));
            bsp.Visibility = ReadVisibility(bytes, offsets[16]);
''', 'face lightmap metadata and lighting lumps')

replace_once(path,
'''            foreach(var model in bsp.Models)
                if(!Range(model.Face,model.FaceCount,bsp.Faces.Count)||!Range(model.Brush,model.BrushCount,bsp.Brushes.Count))throw new InvalidDataException("Invalid BSP model ranges.");
            foreach(var face in bsp.Faces)
''',
'''            foreach(var model in bsp.Models)
                if(!Range(model.Face,model.FaceCount,bsp.Faces.Count)||!Range(model.Brush,model.BrushCount,bsp.Brushes.Count))throw new InvalidDataException("Invalid BSP model ranges.");
            for (int nodeIndex = 0; nodeIndex < bsp.Nodes.Count; nodeIndex++)
            {
                Q3Node node = bsp.Nodes[nodeIndex];
                if (node.Plane < 0 || node.Plane >= bsp.Planes.Count)
                    throw new InvalidDataException("Invalid BSP node plane.");
                foreach (int child in node.Children)
                {
                    if (child >= 0 && child >= bsp.Nodes.Count)
                        throw new InvalidDataException("Invalid BSP node child.");
                    if (child < 0 && -child - 1 >= bsp.Leafs.Count)
                        throw new InvalidDataException("Invalid BSP node leaf child.");
                }
            }
            foreach (Q3Leaf leaf in bsp.Leafs)
            {
                if (!Range(leaf.FirstLeafSurface, leaf.LeafSurfaceCount, bsp.LeafSurfaces.Count)
                    || !Range(leaf.FirstLeafBrush, leaf.LeafBrushCount, bsp.LeafBrushes.Count))
                    throw new InvalidDataException("Invalid BSP leaf ranges.");
                if (leaf.Cluster < -1 || bsp.Visibility != null && leaf.Cluster >= bsp.Visibility.ClusterCount)
                    throw new InvalidDataException("Invalid BSP leaf cluster.");
            }
            if (bsp.LeafSurfaces.Any(index => index < 0 || index >= bsp.Faces.Count)
                || bsp.LeafBrushes.Any(index => index < 0 || index >= bsp.Brushes.Count))
                throw new InvalidDataException("Invalid BSP leaf references.");
            foreach (Q3Fog fog in bsp.Fogs)
                if (fog.Brush < -1 || fog.Brush >= bsp.Brushes.Count
                    || fog.VisibleSide < -1 || fog.Brush >= 0 && fog.VisibleSide >= bsp.Brushes[fog.Brush].SideCount)
                    throw new InvalidDataException("Invalid BSP fog reference.");
            foreach(var face in bsp.Faces)
''', 'extended BSP reference validation')

replace_once(path,
'''                if(face.Type==2&&(face.Size[0]<3||face.Size[1]<3||face.Size[0]>129||face.Size[1]>129||(long)face.Size[0]*face.Size[1]>face.VertexCount))
                    throw new InvalidDataException("Invalid BSP patch dimensions.");
                for(int i=face.MeshVert;i<face.MeshVert+face.MeshVertCount;i++)
''',
'''                if(face.Type==2&&(face.Size[0]<3||face.Size[1]<3||face.Size[0]>129||face.Size[1]>129||(long)face.Size[0]*face.Size[1]>face.VertexCount))
                    throw new InvalidDataException("Invalid BSP patch dimensions.");
                if (face.Effect < -1 || face.Effect >= bsp.Fogs.Count)
                    throw new InvalidDataException("Invalid BSP surface fog reference.");
                if (face.Lightmap < -1 || face.Lightmap >= bsp.Lightmaps.Count)
                    throw new InvalidDataException("Invalid BSP surface lightmap reference.");
                if (face.LightmapSize.Any(value => value < 0 || value > 128)
                    || face.LightmapStart.Any(value => value < 0 || value > 128))
                    throw new InvalidDataException("Invalid BSP lightmap rectangle.");
                for(int i=face.MeshVert;i<face.MeshVert+face.MeshVertCount;i++)
''', 'surface lighting validation')

replace_once(path,
'''            if(bsp.Vertices.Any(v=>v.Position.Concat(v.Surface).Concat(v.Normal).Any(x=>!float.IsFinite(x)))
                ||bsp.Planes.Any(p=>!float.IsFinite(p.X)||!float.IsFinite(p.Y)||!float.IsFinite(p.Z)||!float.IsFinite(p.Distance)))
''',
'''            if(bsp.Vertices.Any(v=>v.Position.Concat(v.Surface).Concat(v.Lightmap).Concat(v.Normal).Any(x=>!float.IsFinite(x)))
                ||bsp.Faces.Any(f => f.LightmapOrigin.Concat(f.LightmapVectors.SelectMany(v => v)).Any(x => !float.IsFinite(x)))
                ||bsp.Planes.Any(p=>!float.IsFinite(p.X)||!float.IsFinite(p.Y)||!float.IsFinite(p.Z)||!float.IsFinite(p.Distance)))
''', 'lighting finite validation')

replace_once(path,
'''        private static IReadOnlyList<T> ReadLump<T>(BinaryReader reader, (int offset, int length) lump,
''',
'''        private static IReadOnlyList<Q3LightmapPage> ReadLightmaps(byte[] bytes,
            (int offset, int length) lump, CancellationToken cancellation)
        {
            const int width = 128;
            const int height = 128;
            const int pageBytes = width * height * 3;
            if (lump.length % pageBytes != 0)
                throw new InvalidDataException("BSP lightmap lump has a partial page.");
            var result = new List<Q3LightmapPage>(lump.length / pageBytes);
            for (int i = 0; i < lump.length / pageBytes; i++)
            {
                cancellation.ThrowIfCancellationRequested();
                var rgb = new byte[pageBytes];
                Buffer.BlockCopy(bytes, lump.offset + i * pageBytes, rgb, 0, pageBytes);
                result.Add(new Q3LightmapPage(i, width, height, rgb));
            }
            return result;
        }

        private static Q3Visibility? ReadVisibility(byte[] bytes, (int offset, int length) lump)
        {
            if (lump.length == 0) return null;
            if (lump.length < 8) throw new InvalidDataException("BSP visibility lump is truncated.");
            int clusters = BitConverter.ToInt32(bytes, lump.offset);
            int bytesPerCluster = BitConverter.ToInt32(bytes, lump.offset + 4);
            if (clusters < 0 || clusters > 65536 || bytesPerCluster < 0 || bytesPerCluster > 8192
                || (long)clusters * bytesPerCluster != lump.length - 8)
                throw new InvalidDataException("Invalid BSP visibility dimensions.");
            var data = new byte[lump.length - 8];
            Buffer.BlockCopy(bytes, lump.offset + 8, data, 0, data.Length);
            return new Q3Visibility(clusters, bytesPerCluster, data);
        }

        private static IReadOnlyList<T> ReadLump<T>(BinaryReader reader, (int offset, int length) lump,
''', 'lightmap and visibility helpers')

replace_once(path,
'''    public record Q3BrushSide(int Plane, int Texture);
    public record Q3Vertex(float[] Position, float[] Surface, float[] Normal, byte[] Color);
    public record Q3Face(int Texture, int Effect, int Type, int Vertex, int VertexCount,
        int MeshVert, int MeshVertCount, float[] Normal, int[] Size);
    public record Q3Model(float[] Mins, float[] Maxs, int Face, int FaceCount, int Brush, int BrushCount);
}
''',
'''    public record Q3BrushSide(int Plane, int Texture);
    public record Q3Node(int Plane, int[] Children, int[] Mins, int[] Maxs);
    public record Q3Leaf(int Cluster, int Area, int[] Mins, int[] Maxs,
        int FirstLeafSurface, int LeafSurfaceCount, int FirstLeafBrush, int LeafBrushCount);
    public record Q3Fog(string Name, int Brush, int VisibleSide);
    public record Q3Vertex(float[] Position, float[] Surface, float[] Lightmap,
        float[] Normal, byte[] Color);
    public record Q3Face(int Texture, int Effect, int Type, int Vertex, int VertexCount,
        int MeshVert, int MeshVertCount, int Lightmap, int[] LightmapStart,
        int[] LightmapSize, float[] LightmapOrigin, float[][] LightmapVectors,
        float[] Normal, int[] Size);
    public record Q3Model(float[] Mins, float[] Maxs, int Face, int FaceCount, int Brush, int BrushCount);
    public record Q3LightmapPage(int Id, int Width, int Height, byte[] Rgb24);
    public record Q3LightGridSample(byte[] Ambient, byte[] Directed, byte Latitude, byte Longitude);

    public sealed record Q3Visibility(int ClusterCount, int BytesPerCluster, byte[] Data)
    {
        public bool IsVisible(int fromCluster, int toCluster)
        {
            if (fromCluster < 0 || toCluster < 0
                || fromCluster >= ClusterCount || toCluster >= ClusterCount)
                return true;
            int index = fromCluster * BytesPerCluster + (toCluster >> 3);
            return index >= 0 && index < Data.Length && (Data[index] & (1 << (toCluster & 7))) != 0;
        }
    }
}
''', 'Q3 fidelity records')

print('Complete Quake 3 BSP fidelity parser integrated')
