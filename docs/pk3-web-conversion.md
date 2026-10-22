# Website Quake PK3 direct upload

`/maps/upload` accepts both `.ppmap` and `.pk3` through one chooser or drop
zone. It checks archive structure before routing: a valid root `manifest.json`
identifies a Project Prime package, while a ZIP with playable `maps/*.bsp`
entries and no root manifest is treated as a Quake PK3. The standalone
`/maps/upload/pk3` route remains available for direct links. Selecting a PK3
never publishes it without a separate conversion and creator approval.

It requires a separately deployed `tools/pk3-upload` service from
AntiNotAnti/Project-Prime PRs PK3-01 and PK3-02. By default, the website
looks for the converter under `/pk3/` on its existing
`NEXT_PUBLIC_MAP_SERVICE_URL` origin (currently falling back to the same
`https://maps.rebooty.xyz/` origin used by the Community catalog).
You can override it with `NEXT_PUBLIC_PK3_CONVERSION_URL` set to the real
HTTPS converter base path, e.g. `https://your-map-host.example/pk3/`.

**Neither URL proves the service is running.** The browser issues a bounded
`GET /pk3/health` request on first use and requires the isolated conversion
service's `{"ok":true}` response, over a CORS-approved HTTPS endpoint,
before allowing source uploads. If the service is absent, misrouted,
unreachable, or denies CORS, the UI remains disabled with a retry action,
while `.ppmap` publishing continues normally. Selecting a fallback URL never
uploads PK3 bytes to the Community package API or publishes automatically.
The website does not deploy the conversion sidecar or change VPS DNS/TLS.

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
