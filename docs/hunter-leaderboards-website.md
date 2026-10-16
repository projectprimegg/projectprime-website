# Public Hunter directory and career leaderboards

The website uses the read-only `hunter-leaderboard` Supabase Edge Function.
All statistics come from Project Prime's existing authoritative server-
accepted career projection; browsers cannot submit wins, kills or RP.

## Public-by-default policy

- `/hunters` discovers Auth-owned Hunter Licenses by nickname unless their
  owner explicitly opted out. This includes new Licenses with no eligible
  matches; orphan legacy records without sign-in access remain hidden.
- `/hunters/{opaqueId}` displays a limited public card: nickname, favorite
  Hunter and accepted career totals. Opted-out profile links return 404.
- `/leaderboards` shows **only accepted career** records with eligible matches.
  Opted-out Hunters may rank anonymously, without name or public ID.
- No public endpoint exposes Supabase Auth UUID, email, credentials, cosmetic
  loadout, detailed recent match logs or raw reports.

Ranking sorts include wins, kills, matches, K/D, win rate and headshots,
with canonical native game-mode filters. "Ranked" RP is never synthesized
when the trusted rating transaction registry is empty.

## Fresh privacy behavior

All public requests use a no-store response policy: direct profiles,
directory search and ranked pages. A newly opted-out Hunter is not retained
in Next.js data cache for subsequent requests. The Edge also returns
no-store. Historical browser screenshots and pages loaded before opt-out
cannot be rescinded retroactively.

Every signed-in Hunter can change visibility on the website's /license page;
guest Hunters can use the game's Account screen. The Supabase owner-only RPC
stores explicit false to opt out and explicit true to rejoin. Neither path
changes career results or match validity.

## Production

Release the in-game opt-out and website copy **before** changing the live
database function and Edge API, since existing unset accounts become
publicly discoverable when the migration is applied.
The website only uses publishable Supabase keys; Postgres and service-role
credentials stay inside the Supabase backend.

Accept changes only after confirming zero-game public profiles, opt-out
404, anonymous ranking, name-search exclusions, per-mode scores,
guest opt-out, and private-field non-disclosure. Validate the exact
website head SHA in CI and real browser behavior after deployment.
