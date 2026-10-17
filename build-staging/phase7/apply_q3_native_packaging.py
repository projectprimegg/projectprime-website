from pathlib import Path
import sys


def rep(path, before, after, label):
    text = path.read_text(encoding='utf-8')
    count = text.count(before)
    if count != 1:
        raise RuntimeError(f'{label}: expected one anchor, found {count} in {path}')
    path.write_text(text.replace(before, after, 1), encoding='utf-8', newline='\n')


if len(sys.argv) != 2:
    raise SystemExit('usage: apply_q3_native_packaging.py <project-root>')
root = Path(sys.argv[1]).resolve()
mapgen = root / 'src/MphRead/Mods/MapGen'

fidelity = mapgen / 'Q3NativeFidelity.cs'
rep(fidelity,
'''            LightingMode = bsp.Lightmaps.Count > 0
                ? UniversalLightingMode.SourceBakedAndRealtime
                : UniversalLightingMode.VertexFallback
''',
'''            LightingMode = bsp.Lightmaps.Count > 0
                ? UniversalLightingMode.SourceBakedAndRealtime
                : UniversalLightingMode.VertexFallback,
            LightmapOverlayAsset = bsp.Lightmaps.Count > 0
                ? "native/q3/lightmap-overlay.pplm" : null
''', 'lightmap overlay metadata')
rep(fidelity,
'''                Asset = $"native/q3/lightmaps/{page.Id:D4}.rgb",
''',
'''                Asset = $"native/q3/lightmaps/{page.Id:D4}.png",
''', 'portable lightmap extension')

builder = mapgen / 'Packaging/MapPackageBuilder.cs'
rep(builder,
'''                if (definition.Import is { } import)
                {
                    string level = import.Resolve() ?? throw new MapAuthoringException(
                        "FP-MAP-005", "Imported level is missing.");
                    AddGenerated("import/level.bsp",
                        Q3Bsp.Trim(Q3Bsp.ReadLevel(level, import.MapName)));
''',
'''                if (definition.Import is { } import)
                {
                    string level = import.Resolve() ?? throw new MapAuthoringException(
                        "FP-MAP-005", "Imported level is missing.");
                    Q3Bsp sourceBsp = Q3Bsp.Load(level, import.MapName, cancellation);
                    definition.NativeFidelity = Q3NativeFidelity.Build(sourceBsp,
                        import.UnitsPerUnit, import.MapName ?? Path.GetFileName(level));
                    foreach (Q3LightmapPage page in sourceBsp.Lightmaps)
                    {
                        UniversalLightmapPage metadata = definition.NativeFidelity.Lightmaps
                            .Single(value => value.Id == page.Id);
                        AddGenerated(metadata.Asset, Q3LightmapPackaging.EncodePng(page,
                            metadata.OverbrightBits, rendererOverbrightBits: 0,
                            exposure: metadata.Exposure));
                    }
                    if (definition.NativeFidelity.LightmapOverlayAsset is { } overlayAsset)
                    {
                        MapDefinition overlayDefinition = MapProjectSerializer.Clone(definition);
                        BuiltMap overlayMap = Q3Import.Build(
                            overlayDefinition, verbose: false, cancellation);
                        Q3LightmapOverlayAsset.Overlay overlay =
                            Q3LightmapOverlayAsset.Build(overlayMap, definition.ScaleFactor);
                        if (overlay.VertexCount > 0)
                            AddGenerated(overlayAsset, Q3LightmapOverlayAsset.Encode(overlay));
                        else
                            definition.NativeFidelity.LightmapOverlayAsset = null;
                    }
                    if (sourceBsp.Visibility != null
                        && definition.NativeFidelity.Visibility?.Asset is { } visibilityAsset)
                        AddGenerated(visibilityAsset, Q3VisibilityAsset.Encode(sourceBsp.Visibility));
                    AddGenerated("import/level.bsp",
                        Q3Bsp.Trim(Q3Bsp.ReadLevel(level, import.MapName)));
''', 'generated Q3 fidelity assets')

reader = mapgen / 'Packaging/MapPackageReader.cs'
rep(reader,
'''                if (ext is not (".json" or ".bsp" or ".obj" or ".tex" or ".png"
                    or ".jpg" or ".jpeg" or ".tga" or ".ktx2" or ".ogg" or ".wav" or ".mp3"))
''',
'''                if (ext is not (".json" or ".bsp" or ".obj" or ".tex" or ".png"
                    or ".jpg" or ".jpeg" or ".tga" or ".ktx2" or ".pvs" or ".pplm"
                    or ".ogg" or ".wav" or ".mp3"))
''', 'native fidelity package extensions')
rep(reader,
'''            if (definition.Import is { } import)
            {
                if (!entries.ContainsKey(CanonicalName(import.Source)))
                    throw new InvalidDataException("Packaged BSP is missing.");
                if (!import.Source.EndsWith(".bsp", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Package import must reference a BSP.");
                if (!string.IsNullOrEmpty(import.Textures)
                    && Find(entries, import.Textures) == null)
                    throw new InvalidDataException("Packaged texture pack is missing.");
            }
''',
'''            if (definition.Import is { } import)
            {
                if (!entries.ContainsKey(CanonicalName(import.Source)))
                    throw new InvalidDataException("Packaged BSP is missing.");
                if (!import.Source.EndsWith(".bsp", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Package import must reference a BSP.");
                if (!string.IsNullOrEmpty(import.Textures)
                    && Find(entries, import.Textures) == null)
                    throw new InvalidDataException("Packaged texture pack is missing.");
            }
            if (definition.NativeFidelity is { } native)
            {
                IReadOnlyList<string> problems = native.Validate();
                if (problems.Count > 0)
                    throw new InvalidDataException("Invalid native-fidelity metadata: " + problems[0]);
                foreach (UniversalLightmapPage page in native.Lightmaps)
                {
                    if (!page.Asset.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                        || !entries.ContainsKey(CanonicalName(page.Asset)))
                        throw new InvalidDataException("Packaged lightmap page is missing or invalid.");
                }
                if (native.LightmapOverlayAsset is { } overlay)
                {
                    if (!entries.ContainsKey(CanonicalName(overlay)))
                        throw new InvalidDataException("Packaged lightmap overlay is missing.");
                    _ = Q3LightmapOverlayAsset.Decode(ReadRequired(entries, overlay));
                }
                if (native.Visibility?.Asset is { } pvs)
                {
                    if (!pvs.EndsWith(".pvs", StringComparison.OrdinalIgnoreCase)
                        || !entries.ContainsKey(CanonicalName(pvs)))
                        throw new InvalidDataException("Packaged visibility data is missing or invalid.");
                    _ = Q3VisibilityAsset.Decode(ReadRequired(entries, pvs));
                }
            }
''', 'native fidelity package references')

validator = mapgen / 'Validation/MapValidator.cs'
rep(validator,
'''            MapAssets.Validate(d,r,checkSources);
''',
'''            MapAssets.Validate(d,r,checkSources);
            if (d.NativeFidelity is { } nativeFidelity)
                foreach (string problem in nativeFidelity.Validate())
                    r.Error("FP-MAP-NATIVE", problem);
''', 'native fidelity validation')

print('Quake lightmap, overlay and PVS packaging integrated')
