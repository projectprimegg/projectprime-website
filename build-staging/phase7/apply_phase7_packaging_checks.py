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

            UniversalNativeMapData fidelity = Q3NativeFidelity.Build(bsp, 35f, "phase7-fixture");
'''
if text.count(before) != 1:
    raise RuntimeError('Phase 7 packaging check anchor mismatch')
path.write_text(text.replace(before, after, 1), encoding='utf-8', newline='\n')
