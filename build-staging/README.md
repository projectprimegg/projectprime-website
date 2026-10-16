# Universal Map Studio v20 build staging

This branch is isolated from the website's main branch and exists only to build a Project Prime Windows test client.

It reconstructs the custom source from the exact official Project Prime v0.1.53 commit using the recovered v19/v20 source patches, then applies the Universal AI Map Studio Phase 2 patch and publishes a self-contained win-x64 client.

The CI reconstruction intentionally uses the code-only v19 patch because the full v19 patch contains large bundled custom font/audio assets that are not needed to compile or test the Universal Map Studio path. The resulting artifact should be treated as a Map Studio engineering test build rather than the final asset-complete v20 client.
