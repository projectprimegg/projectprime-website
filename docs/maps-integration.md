# Community map website integration

The `/maps` public catalog uses the **existing** Project Prime map
service's `GET /v2/maps`, `GET /v2/maps/{mapId}`, and revision APIs. It
does not store a parallel map index or rewrite immutable map identities.

Configure `NEXT_PUBLIC_MAP_SERVICE_URL` (HTTPS URL with trailing slash)
on Vercel and for local development. The current game default is
`https://maps.rebooty.xyz/`, used by the site if this variable is unset.
The website must use the exact Community origin configured for the game.

Public pages are server rendered and paginated. A same-origin preview
endpoint fetches only bounded PNG previews from `/packages/{sha}/preview`,
not full map archives. Package links target `/packages/{sha}` directly.

The map site deliberately shows a manual install instruction instead of
claiming to register `projectprime://` deep links: URL scheme handlers have
not been added to supported game installers. Multiplayer already has its
own exact-hash content preparation and Ready barrier.

## Acceptance

- A new published map appears under `/maps` and its stable map-ID detail page.
- Search, sort, pagination and valid public revision downloads work.
- Unknown or invalid map IDs return 404; missing service displays a retry state.
- Missing/invalid thumbnails cannot cause the website to download an entire
  .ppmap archive. Broken thumbnails have a styled background fallback.
- Build and TypeScript CI pass; no game package hashes, protocol or map
  server catalog are modified.
