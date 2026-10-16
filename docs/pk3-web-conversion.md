# Website Quake PK3 direct upload

`/maps/upload/pk3` provides a creator-authorized, guided Quake source
conversion and publication process.

It requires a separately deployed `tools/pk3-upload` service from
AntiNotAnti/Project-Prime PRs PK3-01 and PK3-02. Set the Vercel public
configuration `NEXT_PUBLIC_PK3_CONVERSION_URL` to the already-deployed
and TLS-verified conversion API path, for example
`https://maps.example.com/pk3/`. The configuration is intentionally
**absent by default**. Until the service exists, the UI shows a clear
not-enabled state and directs creators to local Studio import.

A creator signs into the same Hunter License account already used for
Community `.ppmap` publishing. File ZIP metadata is scanned locally to
select a `maps/*.bsp` level, after which the browser hashes the source
incrementally and uploads it directly to the PK3 service in 8 MiB
resumable chunks with a short-lived `ppm1` ticket.

The worker verifies the exact source SHA, converts with existing
`Q3ImportService`, validates the `.ppmap` with strict
`MapPackageReader`, and returns a creator-only result. The browser
displays conversion warnings and requires an explicit publication action.
When approved it fetches the package, verifies its SHA and MapId,
then sends the **same immutable .ppmap bytes** through the website's
existing Community publisher, under the original Hunter License owner.

This is not a new map catalog and cannot impersonate another creator.
The website never proxies large source files through Vercel; the worker
never receives Supabase long-lived service credentials, and no file
goes public just because conversion succeeded.

Limits: 128 MiB input .pk3, 512 MiB output .ppmap (large browser downloads
may be memory intensive), 24h private conversion retention. Browser
upload pauses/resumes with the same source. Server conversion proceeds
after upload is complete even if the browser disconnects. Source level
selection and new runtime map name are immutable once upload starts
for a given Hunter/source pair.

Publishing can initially use visibility `Draft` for compatibility
testing. Public publication requires source redistribution permission and
actual map collision, spawn, asset and multiplayer acceptance. Copyrighted
Quake base-game archives are not supplied by the website.

Before production: deploy backend worker and TLS proxy, configure the
exact allowlisted website origins, run real cross-device and two-creator
auth tests, and verify full local map install + 1v1/FFA + replay parity.
