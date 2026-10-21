# Uploading .ppmap or .pk3 files from projectprime.gg

This website performs the same creator-authorized, exact-hash publication
protocol as the desktop `MapCommunityClient`. Packages go directly to the
Community service, **never through a Vercel function**, because their supported
maximum is 512 MiB.

1. Open `/maps/upload` without creating an account. Signed-in Hunters publish under their existing identity. Guests can select and inspect either file type immediately; clicking **Publish as Guest** or **Convert PK3 as Guest** creates a temporary Supabase anonymous session without an email or password.
2. Select a `.ppmap` or `.pk3` in the single file chooser on `/maps/upload`. The website recognizes the extension and validates the archive before proceeding. Quake `.pk3` files use the isolated converter and require explicit rights confirmation, BSP selection, and converted-map review. `.ppmap` files follow the existing direct Community upload flow.
3. The browser reads only the bounded ZIP manifest and incrementally hashes
   the archive in 4 MiB blocks. This is display/identity preflight, **not**
   a substitute for server-side strict package validation.
4. `/api/maps/ticket` mints a short-lived `ppm1` creator Bearer ticket.
5. `GET /v2/maps?mine=true` finds the creator's known Map ID and latest
   immutable revision. New publications pass `existingMap=false`; revision
   uploads carry `existingMap=true` and `expectedParentHash`.
6. `POST /uploads/{sha}` reserves storage and returns the authoritative
   byte offset. The browser sends 8 MiB or smaller sequential PUT chunks,
   adapts to 413, rechecks offset on 409 and retries 429/503.
7. `POST /uploads/{sha}/complete` performs server-side package, identity,
   ownership and revision validation before publishing.
8. The website links to the stable `/maps/{MapId}` detail page. Game clients
   continue to verify the exact package identity independently.

Unlisted maps are hidden from normal discovery but can remain accessible to anyone who has an exact package hash/link; use Draft for private work.

Guest maps are owned by the browser's temporary Supabase identity, not by a public uploader. Guest authors can manage revisions while the browser session remains intact. Signing out, clearing site data or switching devices can permanently lose map ownership. For recoverable ownership, link that guest identity to a verified login before losing its session; signing into a different account does not transfer the maps.\n\nA paused/failed upload can be resumed by choosing the **same file** and
submitting it again. Server partials expire under existing retention policy.
File bytes and creator tokens are never stored in website databases or
localStorage by the uploader. Supabase manages its own browser auth session.

## Safety / future work

- Guest upload is not an unauthenticated API. The browser obtains a Supabase
  anonymous identity only on explicit submission, then the existing scoped
  `ppm1` ticket and server-side ownership, quotas and ZIP checks apply.
- Disposable guest identities can evade per-creator quotas. Operators should
  keep Supabase anonymous sign-in rate limits enabled and use network-edge
  upload throttling and Community moderation. Add a CAPTCHA token flow to
  guest sign-in before enabling CAPTCHA/Turnstile in Supabase. Never expose
  a master publisher token to browser clients.


- The UI deliberately does not bypass stale-parent revision conflicts.
- ZIP manifests are untrusted preflight input. Only the Community service
  can approve and publish the actual `.ppmap` archive.
- The main `/maps/upload` chooser routes `.pk3` archives to the isolated
  converter; the original `/maps/upload/pk3` workflow remains accessible.
  PK3 conversion is enabled **only after** the backend is deployed and
  `NEXT_PUBLIC_PK3_CONVERSION_URL` is configured. `.ppmap` publishing remains
  available even when the converter is offline, with its strict contract unchanged.
- Verify the map service has deployed approved CORS preflight support and a
  body limit compatible with browser chunks.
- Validate the production Supabase sign-in redirect allowlist and public
  project credentials before enabling website publishing in production.
