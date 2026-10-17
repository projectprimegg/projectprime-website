from pathlib import Path
import sys

if len(sys.argv) != 2:
    raise SystemExit('usage: apply_phase7_packaging_checks.py <project-root>')
path = Path(sys.argv[1]).resolve() / 'tools/phase7-check/Program.cs'
text = path.read_text(encoding='utf-8')
before = '''            Check(Q3LightmapProcessing.ToRgba32(overbright).Length
                == Q3LightmapProcessing.Width * Q3LightmapProcessing.Height * 4,
                "RGB lightmap expands to RGBA once");

            UniversalNativeMapData fidelity = Q3NativeFidelity.Build(bsp, 35f, "phase7-fixture");
'''
after = '''            Check(Q3LightmapProcessing.ToRgba32(overbright).Length
                == Q3LightmapProcessing.Width * Q3LightmapProcessing.Height * 4,
                "RGB lightmap expands to RGBA once");
            byte[] lightmapPng = Q3LightmapPackaging.EncodePng(bsp.Lightmaps[0]);
            Check(lightmapPng.AsSpan(0, 8).SequenceEqual(
                    new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
                "lightmap packages as PNG");
            Check(MphRead.Mods.Render.ModernTextureAsset.ProbeDimensions(lightmapPng)
                    == (Q3LightmapProcessing.Width, Q3LightmapProcessing.Height),
                "packaged lightmap dimensions validate");
            byte[] pvsAsset = Q3VisibilityAsset.Encode(bsp.Visibility!);
            Q3Visibility decodedPvs = Q3VisibilityAsset.Decode(pvsAsset);
            Check(decodedPvs.IsVisible(0, 0), "PVS sidecar round-trip");

            var overlayDefinition = new MapDefinition { Name = "OVERLAY_TEST", ScaleFactor = 0 };
            var overlayMap = new BuiltMap(overlayDefinition);
            var overlayFace = new BuiltFace(
                new[]
                {
                    new OpenTK.Mathematics.Vector3(0, 0, 0),
                    new OpenTK.Mathematics.Vector3(1.0001f, 0, 0),
                    new OpenTK.Mathematics.Vector3(0, 1.0001f, 0)
                },
                new[]
                {
                    new OpenTK.Mathematics.Vector2(0, 0),
                    new OpenTK.Mathematics.Vector2(1, 0),
                    new OpenTK.Mathematics.Vector2(0, 1)
                },
                OpenTK.Mathematics.Vector3.UnitZ, 0, 1f)
            {
                LightmapPage = 0
            };
            overlayFace.AdditionalTexcoords[1] = new[]
            {
                new OpenTK.Mathematics.Vector2(0, 0),
                new OpenTK.Mathematics.Vector2(1, 0),
                new OpenTK.Mathematics.Vector2(0, 1)
            };
            overlayMap.Faces.Add(overlayFace);
            Q3LightmapOverlayAsset.Overlay overlay =
                Q3LightmapOverlayAsset.Build(overlayMap, overlayDefinition.ScaleFactor);
            byte[] overlayBytes = Q3LightmapOverlayAsset.Encode(overlay);
            Q3LightmapOverlayAsset.Overlay overlayRoundTrip =
                Q3LightmapOverlayAsset.Decode(overlayBytes);
            Check(overlayRoundTrip.Groups.Count == 1
                    && overlayRoundTrip.VertexCount == 3
                    && overlayRoundTrip.Groups[0].Page == 0,
                "depth-matched lightmap overlay round-trip");
            bool badOverlayRejected = false;
            try { _ = Q3LightmapOverlayAsset.Decode(overlayBytes[..^1]); }
            catch (InvalidDataException) { badOverlayRejected = true; }
            Check(badOverlayRejected, "truncated lightmap overlay rejected safely");

            UniversalNativeMapData fidelity = Q3NativeFidelity.Build(bsp, 35f, "phase7-fixture");
'''
if text.count(before) != 1:
    raise RuntimeError('Phase 7 packaging check anchor mismatch')
path.write_text(text.replace(before, after, 1), encoding='utf-8', newline='\n')
