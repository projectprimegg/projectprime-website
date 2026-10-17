from pathlib import Path
import sys


def rep(path, before, after, label):
    text = path.read_text(encoding='utf-8')
    count = text.count(before)
    if count != 1:
        raise RuntimeError(f'{label}: expected one anchor, found {count}')
    path.write_text(text.replace(before, after, 1), encoding='utf-8', newline='\n')


if len(sys.argv) != 2:
    raise SystemExit('usage: apply_q3_import_lightmaps.py <project-root>')
root = Path(sys.argv[1]).resolve()
path = root / 'src/MphRead/Mods/MapGen/Q3Import.cs'

rep(path,
'''            var map = new BuiltMap(def);
            float unit = import.UnitsPerUnit;
''',
'''            var map = new BuiltMap(def);
            float unit = import.UnitsPerUnit;
            map.NativeFidelity = Q3NativeFidelity.Build(bsp, unit, import.MapName ?? import.Source);
            def.NativeFidelity = map.NativeFidelity;
''', 'attach Q3 native fidelity')

rep(path,
'''                foreach (BuiltFace built in patch
                    ? Tessellate(bsp, face, unit, width, height, material, sky, import.PatchLevel, cancellation)
                    : Triangles(bsp, face, unit, width, height, material, sky))
''',
'''                foreach (BuiltFace built in patch
                    ? Tessellate(bsp, face, unit, width, height, material, sky,
                        import.PatchLevel, sourceFaceIndex, cancellation)
                    : Triangles(bsp, face, unit, width, height, material, sky, sourceFaceIndex))
''', 'source surface passed to geometry')

rep(path,
'''        private static IEnumerable<BuiltFace> Triangles(Q3Bsp bsp, Q3Face face, float unit,
            int width, int height, int material, bool sky)
''',
'''        private static IEnumerable<BuiltFace> Triangles(Q3Bsp bsp, Q3Face face, float unit,
            int width, int height, int material, bool sky, int sourceSurface)
''', 'triangle signature')

rep(path,
'''                var points = new Vector3[3];
                var uvs = new Vector2[3];
                var vertexNormal = Vector3.Zero;
''',
'''                var points = new Vector3[3];
                var uvs = new Vector2[3];
                var lightmapUvs = new Vector2[3];
                var vertexNormal = Vector3.Zero;
''', 'triangle UV arrays')

rep(path,
'''                    uvs[j] = new Vector2(vertex.Surface[0] * width, vertex.Surface[1] * height);
                    vertexNormal += ToDirection(vertex.Normal);
''',
'''                    uvs[j] = new Vector2(vertex.Surface[0] * width, vertex.Surface[1] * height);
                    lightmapUvs[j] = new Vector2(vertex.Lightmap[0], vertex.Lightmap[1]);
                    vertexNormal += ToDirection(vertex.Normal);
''', 'triangle lightmap coordinates')

rep(path,
'''                yield return MakeFace(points, uvs,
                    TriangleNormal(points, vertexNormal, ToDirection(face.Normal)),
                    material, Shade(shade / 3, sky));
''',
'''                yield return MakeFace(points, uvs, lightmapUvs,
                    TriangleNormal(points, vertexNormal, ToDirection(face.Normal)),
                    material, Shade(shade / 3, sky), face.Lightmap, sourceSurface, face.Effect);
''', 'triangle built-face bindings')

rep(path,
'''        private static IEnumerable<BuiltFace> Tessellate(Q3Bsp bsp, Q3Face face, float unit,
            int width, int height, int material, bool sky, int level, CancellationToken cancellation)
''',
'''        private static IEnumerable<BuiltFace> Tessellate(Q3Bsp bsp, Q3Face face, float unit,
            int width, int height, int material, bool sky, int level, int sourceSurface,
            CancellationToken cancellation)
''', 'patch signature')

rep(path,
'''                    var points = new Vector3[level + 1, level + 1];
                    var uvs = new Vector2[level + 1, level + 1];
                    var normals = new Vector3[level + 1, level + 1];
''',
'''                    var points = new Vector3[level + 1, level + 1];
                    var uvs = new Vector2[level + 1, level + 1];
                    var lightmapUvs = new Vector2[level + 1, level + 1];
                    var normals = new Vector3[level + 1, level + 1];
''', 'patch UV grids')

rep(path,
'''                            Vector3 position = Vector3.Zero;
                            var uv = Vector2.Zero;
                            Vector3 normal = Vector3.Zero;
''',
'''                            Vector3 position = Vector3.Zero;
                            var uv = Vector2.Zero;
                            var lightmapUv = Vector2.Zero;
                            Vector3 normal = Vector3.Zero;
''', 'patch UV accumulators')

rep(path,
'''                                    uv += new Vector2(vertex.Surface[0] * width,
                                        vertex.Surface[1] * height) * weight;
                                    normal += ToDirection(vertex.Normal) * weight;
''',
'''                                    uv += new Vector2(vertex.Surface[0] * width,
                                        vertex.Surface[1] * height) * weight;
                                    lightmapUv += new Vector2(vertex.Lightmap[0], vertex.Lightmap[1]) * weight;
                                    normal += ToDirection(vertex.Normal) * weight;
''', 'patch lightmap interpolation')

rep(path,
'''                            points[a, b] = position;
                            uvs[a, b] = uv;
                            normals[a, b] = normal.LengthSquared < 0.0001f
''',
'''                            points[a, b] = position;
                            uvs[a, b] = uv;
                            lightmapUvs[a, b] = lightmapUv;
                            normals[a, b] = normal.LengthSquared < 0.0001f
''', 'store patch lightmap UVs')

rep(path,
'''                            yield return Cell(points, uvs, normals, shades, material, sky,
                                (a, b), (a, b + 1), (a + 1, b + 1));
                            yield return Cell(points, uvs, normals, shades, material, sky,
                                (a, b), (a + 1, b + 1), (a + 1, b));
''',
'''                            yield return Cell(points, uvs, lightmapUvs, normals, shades,
                                material, sky, face.Lightmap, sourceSurface, face.Effect,
                                (a, b), (a, b + 1), (a + 1, b + 1));
                            yield return Cell(points, uvs, lightmapUvs, normals, shades,
                                material, sky, face.Lightmap, sourceSurface, face.Effect,
                                (a, b), (a + 1, b + 1), (a + 1, b));
''', 'patch cells carry lightmap metadata')

rep(path,
'''        private static BuiltFace Cell(Vector3[,] points, Vector2[,] uvs, Vector3[,] normals,
            float[,] shades, int material, bool sky,
            (int A, int B) p0, (int A, int B) p1, (int A, int B) p2)
''',
'''        private static BuiltFace Cell(Vector3[,] points, Vector2[,] uvs, Vector2[,] lightmapUvs,
            Vector3[,] normals, float[,] shades, int material, bool sky,
            int lightmapPage, int sourceSurface, int sourceFog,
            (int A, int B) p0, (int A, int B) p1, (int A, int B) p2)
''', 'cell signature')

rep(path,
'''            var texcoords = new[] { uvs[p0.A, p0.B], uvs[p1.A, p1.B], uvs[p2.A, p2.B] };
            Vector3 normal = normals[p0.A, p0.B] + normals[p1.A, p1.B] + normals[p2.A, p2.B];
''',
'''            var texcoords = new[] { uvs[p0.A, p0.B], uvs[p1.A, p1.B], uvs[p2.A, p2.B] };
            var lightcoords = new[]
            {
                lightmapUvs[p0.A, p0.B], lightmapUvs[p1.A, p1.B], lightmapUvs[p2.A, p2.B]
            };
            Vector3 normal = normals[p0.A, p0.B] + normals[p1.A, p1.B] + normals[p2.A, p2.B];
''', 'cell lightmap coordinates')

rep(path,
'''            return MakeFace(corners, texcoords, normal, material, Shade(shade, sky));
''',
'''            return MakeFace(corners, texcoords, lightcoords, normal, material,
                Shade(shade, sky), lightmapPage, sourceSurface, sourceFog);
''', 'cell built-face bindings')

rep(path,
'''        private static BuiltFace MakeFace(Vector3[] points, Vector2[] uvs, Vector3 normal,
            int material, float shade)
''',
'''        private static BuiltFace MakeFace(Vector3[] points, Vector2[] uvs,
            Vector2[] lightmapUvs, Vector3 normal, int material, float shade,
            int lightmapPage, int sourceSurface, int sourceFog)
''', 'MakeFace signature')

rep(path,
'''                (points[1], points[2]) = (points[2], points[1]);
                (uvs[1], uvs[2]) = (uvs[2], uvs[1]);
            }
            return new BuiltFace(points, uvs, normal, material, shade);
''',
'''                (points[1], points[2]) = (points[2], points[1]);
                (uvs[1], uvs[2]) = (uvs[2], uvs[1]);
                (lightmapUvs[1], lightmapUvs[2]) = (lightmapUvs[2], lightmapUvs[1]);
            }
            var built = new BuiltFace(points, uvs, normal, material, shade)
            {
                LightmapPage = lightmapPage,
                SourceSurfaceId = sourceSurface,
                SourceFogId = sourceFog
            };
            built.AdditionalTexcoords[1] = lightmapUvs;
            return built;
''', 'MakeFace metadata assignment')

rep(path,
'''                result.AddRange(Tessellate(bsp, face, unit, 1, 1, 0, false,
                    level, cancellation));
''',
'''                result.AddRange(Tessellate(bsp, face, unit, 1, 1, 0, false,
                    level, -1, cancellation));
''', 'collision patch call')

print('Quake render geometry now preserves UV1 and lightmap/source bindings')
