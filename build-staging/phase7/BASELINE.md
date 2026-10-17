# Phase 7 implementation baseline

Created 2026-10-17 from the preserved Phase 6 branch.

## Preserved Project Prime staging baseline

- Repository: `projectprimegg/projectprime-website`
- Base branch: `ums-zenith-port-phase6`
- Base commit: `698e25c941f29ac47fc76ffac06609180f934009`
- Feature branch: `feature/phase7-universal-native-map-fidelity`

The Phase 6 packed-scene, Map Studio, AI Map Engineer, real-time-lighting, and all-hunter collision-certification staging files remain in history and are not replaced by a whole-branch merge.

## Reference heads verified before editing

- `AntiNotAnti/Project-Prime` `main`: `d51e8637fbaa7f2c6313cf3589bf11ea68256b47`
- `liveteklol/Fruity-Prime` `master`: `2ee7ff631af36b5d158fae6bc451dec7822da82b`
- `Zection6V/Fruity-Prime` `main`: `697b6e66fcaab7b382c8a7f557b4eb9c49f87c22`
- `Zection6V/Fruity-Prime` pull request 20 was merged at `9dee5fd91c9332c766ea0cd70de3aadd881caf0a`; it is an old broad merge and is not used as the Phase 7 source baseline.

## Integration rule

Phase 7 selectively ports or extends only the required map-import, lighting, material, collision, renderer, editor, test, and packaging systems. It does not merge either Fruity Prime repository wholesale.

## First implementation slice

1. Add reusable Universal native-fidelity data contracts.
2. Preserve multiple UV sets and a dedicated lightmap UV/page on built faces.
3. Parse and validate all relevant Quake 3 BSP 46 lumps, including nodes, leaves, fog, lightmaps, light grid, and PVS.
4. Add deterministic overbright/lightmap processing helpers.
5. Add legal synthetic fixtures and headless acceptance checks.
6. Compile against current Project Prime main before adding runtime OpenGL composition, package assets, Studio controls, and source-entity translation.
