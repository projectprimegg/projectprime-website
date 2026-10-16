from __future__ import annotations
import base64, shutil, sys
from pathlib import Path

def replace_once(path: Path, before: str, after: str, label: str) -> None:
    text=path.read_text(encoding='utf-8')
    count=text.count(before)
    if count!=1: raise RuntimeError(f'{label}: expected one anchor, found {count} in {path}')
    path.write_text(text.replace(before,after,1),encoding='utf-8',newline='\n')

def main():
    if len(sys.argv)!=3: raise SystemExit('usage: apply_zenith_phase6.py <staging> <prime-root>')
    staging=Path(sys.argv[1]).resolve(); root=Path(sys.argv[2]).resolve()
    mapgen=root/'src/MphRead/Mods/MapGen'; gui=root/'src/MphRead/Mods/Launcher/Gui'
    shutil.copy2(staging/'MapPackedScene.cs',mapgen/'MapPackedScene.cs')
    cert = ''.join((staging/f'certification.b64.part-{part}').read_text(encoding='utf-8').strip()
        for part in ('aa','ab','ac','ad'))
    (mapgen/'MapAltFormCertification.cs').write_bytes(base64.b64decode(cert))

    definition=mapgen/'MapDefinition.cs'
    replace_once(definition,
'''        public MapCollision? Collision { get; set; }

        /// <summary>Where to stand the camera for the launcher's map picture.</summary>
''',
'''        public MapCollision? Collision { get; set; }

        /// <summary>
        /// Compact external scene asset used for very large render/collision imports.
        /// It keeps project JSON small while preserving independent collision.
        /// </summary>
        public MapPackedScene? PackedScene { get; set; }

        /// <summary>Where to stand the camera for the launcher's map picture.</summary>
''','MapDefinition packed scene property')
    replace_once(definition,
'''            if (result.Collision != null)
            {
                result.Collision.BaseDirectory = result.BaseDirectory;
                result.Collision.BundlePath = result.BundlePath;
            }
''',
'''            if (result.Collision != null)
            {
                result.Collision.BaseDirectory = result.BaseDirectory;
                result.Collision.BundlePath = result.BundlePath;
            }
            if (result.PackedScene != null)
            {
                result.PackedScene.BaseDirectory = result.BaseDirectory;
                result.PackedScene.BundlePath = result.BundlePath;
            }
''','packed scene source context')

    project=mapgen/'Project/MapProject.cs'
    replace_once(project,
'''            if (copy.Collision != null)
            {
                copy.Collision.BaseDirectory = copy.BaseDirectory;
                copy.Collision.BundlePath = copy.BundlePath;
            }
            return copy;
''',
'''            if (copy.Collision != null)
            {
                copy.Collision.BaseDirectory = copy.BaseDirectory;
                copy.Collision.BundlePath = copy.BundlePath;
            }
            if (copy.PackedScene != null)
            {
                copy.PackedScene.BaseDirectory = copy.BaseDirectory;
                copy.PackedScene.BundlePath = copy.BundlePath;
            }
            return copy;
''','packed scene clone context')

    document=root/'src/MphRead/Mods/MapEditor/MapDocument.cs'
    replace_once(document,
'''                if (replacement.Collision != null) { replacement.Collision.BaseDirectory = replacement.BaseDirectory; replacement.Collision.BundlePath = null; }
''',
'''                if (replacement.Collision != null) { replacement.Collision.BaseDirectory = replacement.BaseDirectory; replacement.Collision.BundlePath = null; }
                if (replacement.PackedScene != null) { replacement.PackedScene.BaseDirectory = replacement.BaseDirectory; replacement.PackedScene.BundlePath = null; }
''','packed scene document replacement context')
    replace_once(document,
'''            if (definition.Collision != null) { definition.Collision.BaseDirectory=definition.BaseDirectory; definition.Collision.BundlePath=null; }
''',
'''            if (definition.Collision != null) { definition.Collision.BaseDirectory=definition.BaseDirectory; definition.Collision.BundlePath=null; }
            if (definition.PackedScene != null) { definition.PackedScene.BaseDirectory=definition.BaseDirectory; definition.PackedScene.BundlePath=null; }
''','packed scene document save context')
    replace_once(document,
'''            if (recovery.Collision != null) { recovery.Collision.BaseDirectory=recovery.BaseDirectory; recovery.Collision.BundlePath=recovery.BundlePath; }
''',
'''            if (recovery.Collision != null) { recovery.Collision.BaseDirectory=recovery.BaseDirectory; recovery.Collision.BundlePath=recovery.BundlePath; }
            if (recovery.PackedScene != null) { recovery.PackedScene.BaseDirectory=recovery.BaseDirectory; recovery.PackedScene.BundlePath=recovery.BundlePath; }
''','packed scene recovery context')

    builder=mapgen/'MapBuilder.cs'
    replace_once(builder,
'''            GeometryCompiler.Add(map, def, cancellation, materialOffset);
        }
''',
'''            GeometryCompiler.Add(map, def, cancellation, materialOffset);
            if (def.PackedScene != null)
                MapPackedSceneCodec.AddTo(map, def, def.PackedScene, cancellation, materialOffset);
        }
''','packed scene compiler integration')

    built=mapgen/'BuiltMap.cs'
    replace_once(built,
'''        public List<MapDiagnostic> ImportDiagnostics { get; } = new();

        public BuiltMap(MapDefinition definition)
''',
'''        public List<MapDiagnostic> ImportDiagnostics { get; } = new();
        public MapAltFormCertificationReport? AltFormCertification { get; set; }
        public MapPackedSceneSummary? PackedSceneSummary { get; set; }

        public BuiltMap(MapDefinition definition)
''','built map certification state')

    replace_once(builder,
'''            if (def.PackedScene != null)
                MapPackedSceneCodec.AddTo(map, def, def.PackedScene, cancellation, materialOffset);
''',
'''            if (def.PackedScene != null)
                map.PackedSceneSummary = MapPackedSceneCodec.AddTo(map, def, def.PackedScene, cancellation, materialOffset);
''','packed scene summary')

    compiler=mapgen/'Build/MapCompiler.cs'
    replace_once(compiler,
'''                MapPacker.ApplyCollision(map, snapshot, verbose: false);
                map.SourceDefinition = definition;
                cancellation.ThrowIfCancellationRequested();
                MapBudgetValidator.Analyze(map, result);
''',
'''                MapPacker.ApplyCollision(map, snapshot, verbose: false);
                if (snapshot.PackedScene is { } packed)
                {
                    var policy = new MapImport
                    {
                        AutoHealCollision = packed.AutoHealCollision,
                        CollisionHealTolerance = packed.CollisionHealTolerance
                    };
                    MapCollisionHealer.Heal(map, policy, cancellation: cancellation);
                    MapCollisionHealer.RepairGameplayObjects(map, snapshot, cancellation);
                    if (packed.RequireAllHunterForms)
                    {
                        map.AltFormCertification = MapAltFormCertification.Certify(map, snapshot, cancellation);
                        if (!map.AltFormCertification.Passed)
                        {
                            foreach (MapPlayerFormFailure failure in map.AltFormCertification.Failures.Take(48))
                                result.Error("FP-MAP-ALT",
                                    $"{failure.Form} {failure.Kind} at {failure.Failure.X:0.###}, {failure.Failure.Y:0.###}, {failure.Failure.Z:0.###}: {failure.Detail}");
                            if (map.AltFormCertification.Failures.Count > 48)
                                result.Error("FP-MAP-ALT", $"{map.AltFormCertification.Failures.Count - 48:N0} additional player-form collision failures were omitted.");
                        }
                    }
                }
                map.SourceDefinition = definition;
                cancellation.ThrowIfCancellationRequested();
                MapBudgetValidator.Analyze(map, result);
''','packed collision healing and certification')

    budget=mapgen/'Validation/MapBudgetValidator.cs'
    replace_once(budget,
'''            Add(result, "Authored assets",map.Definition.Assets.Count,MapPackageReader.MaxEntries-2);
            if (map.Solid.Count == 0) { result.Error("FP-MAP-013", "At least one solid face is required."); return; }
''',
'''            Add(result, "Authored assets",map.Definition.Assets.Count,MapPackageReader.MaxEntries-2);
            if (map.PackedSceneSummary is { } packed)
            {
                Add(result, "Packed scene meshes", packed.Meshes, MapPackedSceneCodec.MaxMeshes);
                Add(result, "Packed scene vertices", packed.Vertices, MapPackedSceneCodec.MaxVertices);
                Add(result, "Packed scene faces", (long)packed.RenderFaces + packed.CollisionFaces, MapPackedSceneCodec.MaxFaces);
            }
            if (map.AltFormCertification is { } forms)
            {
                Add(result, "Player-form certification profiles", forms.Profiles);
                Add(result, "Player-form swept samples", forms.Samples);
                Add(result, "Player-form collision failures", forms.Failures.Count, 1);
                if (!forms.Passed)
                    result.Error("FP-MAP-ALT", forms.Summary);
                else
                    result.Diagnostics.Add(new MapDiagnostic("FP-MAP-ALT", MapDiagnosticSeverity.Info, forms.Summary));
            }
            if (map.Solid.Count == 0) { result.Error("FP-MAP-013", "At least one solid face is required."); return; }
''','packed scene and profile budgets')

    validator=mapgen/'Validation/MapValidator.cs'
    replace_once(validator,
'''            if(d.Partitioning is {} partition)
            {
''',
'''            if (d.PackedScene is { } packed)
            {
                if (String.IsNullOrWhiteSpace(packed.Source))
                    r.Error("FP-MAP-005", "Packed scene source must be a nonempty path.");
                if (!float.IsFinite(packed.CollisionHealTolerance)
                    || packed.CollisionHealTolerance is < 0.00048828125f or > .25f)
                    r.Error("FP-MAP-018", "Packed scene collision-heal tolerance must be 2/4096 through 0.25 units.");
                if (checkSources && packed.ReadBytes() == null)
                    r.Error("FP-MAP-005", "Packed scene source could not be found.");
            }
            if(d.Partitioning is {} partition)
            {
''','packed scene validation')
    replace_once(validator,
'''            if (d.Import == null && d.NativeRoom == null && d.Materials.Count == 0)
                r.Error("FP-MAP-001", "At least one material is required.");
            if (d.Import != null && d.NativeRoom != null)
                r.Error("FP-MAP-008", "A map cannot use both Q3 import and a native-room source.");
''',
'''            if (d.Import == null && d.NativeRoom == null && d.PackedScene == null && d.Materials.Count == 0)
                r.Error("FP-MAP-001", "At least one material is required.");
            int sourceKinds = (d.Import != null ? 1 : 0) + (d.NativeRoom != null ? 1 : 0) + (d.PackedScene != null ? 1 : 0);
            if (sourceKinds > 1)
                r.Error("FP-MAP-008", "A map may use only one external geometry source: Q3, native room, or packed scene.");
''','external source exclusivity')

    package=mapgen/'Packaging/MapPackageBuilder.cs'
    replace_once(package,
'''            if (definition.Collision is { Source.Length: > 0 } collision)
            {
                byte[] bytes = collision.ReadBytes()
                    ?? throw new InvalidDataException("Collision mesh is missing.");
                collision.Source = "collision/mesh.obj";
                entries.Add(collision.Source, bytes);
            }
''',
'''            if (definition.Collision is { Source.Length: > 0 } collision)
            {
                byte[] bytes = collision.ReadBytes()
                    ?? throw new InvalidDataException("Collision mesh is missing.");
                collision.Source = "collision/mesh.obj";
                entries.Add(collision.Source, bytes);
            }
            if (definition.PackedScene is { Source.Length: > 0 } packedScene)
            {
                byte[] bytes = packedScene.ReadBytes()
                    ?? throw new InvalidDataException("Packed scene asset is missing.");
                packedScene.Source = "scene/map.ppmesh";
                entries.Add(packedScene.Source, bytes);
            }
''','package packed scene asset')

    screen=gui/'MapStudioScreen.cs'
    replace_once(screen,
'''            if(project.Definition.Import!=null||project.Definition.NativeRoom!=null)
''',
'''            if(project.Definition.Import!=null||project.Definition.NativeRoom!=null||project.Definition.PackedScene!=null)
''','preview packed scene projects')

    print('Zenith Phase 6 packed-scene and all-hunter certification integrated into',root)

if __name__=='__main__': main()
