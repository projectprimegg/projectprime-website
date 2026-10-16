# Uploading .ppmap files from projectprime.gg

This website performs the same creator-authorized, exact-hash publication
protocol as the desktop `MapCommunityClient`. Packages go directly to the
Community service, **never through a Vercel function**, because their supported
maximum is 512 MiB.

1. Sign into a Hunter License on `/maps/account`.
2. Export a `.ppmap` from Project Prime Studio, then select it on `/maps/upload`.
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

A paused/failed upload can be resumed by choosing the **same file** and
submitting it again. Server partials expire under existing retention policy.
File bytes and creator tokens are never stored in website databases or
localStorage by the uploader. Supabase manages its own browser auth session.

## Safety / future work

- The UI deliberately does not bypass stale-parent revision conflicts.
- ZIP manifests are untrusted preflight input. Only the Community service
  can approve and publish the actual `.ppmap` archive.
- The website does not convert `.pk3` files. Quake conversion needs a separate
  isolated worker and compatibility acceptance phase.
- Verify the map service has deployed approved CORS preflight support and a
  body limit compatible with browser chunks.
- Validate the production Supabase sign-in redirect allowlist and public
  project credentials before enabling website publishing in production.
