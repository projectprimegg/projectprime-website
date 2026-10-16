# Website Hunter License integration

The /license website page shares **exactly the same Supabase project
identity** as Project Prime and the existing /maps creator account UI.
No new password database, player table, or client-side career submission.

## Authenticated snapshot

- Browser Supabase session is passed over same-origin HTTPS to
  POST /api/license/snapshot with Cache-Control: no-store.
- Vercel forwards the JWT to the game's existing
  POST /functions/v1/hunter-license Edge Function.
- That function validates the session server-side and returns the
  authoritative profile, trusted career aggregates, recent accepted
  matches and cosmetic loadouts from the private Prime tables.
- Missing player rows are seeded by the existing Hunter License bridge
  with default name "Hunter". Existing profiles and their names are
  **never overwritten** by the website read.
- No email, access token, database credential or raw match history is
  placed into static HTML, cache, or a public endpoint.
- The website displays the most recent 25 accepted records privately,
  mirroring the game; match results are not publicly searchable.

## Sign-in choices

The account UI defaults to a single passwordless email action, with a clear
password alternative for previously secured licenses plus Google/GitHub/Discord.
Only enabled Supabase Auth providers work. Email signup creates a new identity
when the address is new, and never merges a separate in-game guest automatically.
Secure the existing guest from the game first to retain its career.

Website email and OAuth redirects use canonical `projectprime.gg` URLs rather
than inheriting the development browser origin. The Supabase Auth **Site URL**
must be `https://projectprime.gg`, with exact `/license` and
`/maps/account` callbacks allowed. Ensure mail templates honor the
confirmation URL. Local auth tests require an explicit
`NEXT_PUBLIC_HUNTER_AUTH_ORIGIN` override. Expired/rejected links show a safe
retry message without displaying raw error fragments.

A new user/account cannot automatically claim an unrelated anonymous
game Hunter License. Link the original game identity to the chosen
provider inside the game before signing into the website, or the new
account will display a separate empty career. Never merge users by
matching display names.

## Public profile preference

Visibility is **private by default**. The authenticated website calls
owner-scoped public PostgREST RPCs to read and update the setting:

- project_prime_hunter_visibility()
- project_prime_set_hunter_visibility(p_public boolean)

The RPC is not available until the game repo's privacy migration is
applied. The website keeps visibility disabled and clearly communicates
the missing backend until deployment. The caller never submits a
player UUID and cannot modify career records.

Private users may contribute anonymous leaderboard positions, but
their name, account UUID, profile identifier and recent match list
are not publicly exposed. Public opt-in enables a limited career card.

## Acceptance

1. Sign in with the **same** account as a recovered game License.
2. Compare Hunter ID, nickname, favorite Hunter, career totals and
   accepted recent matches with the game.
3. Confirm a new identity is not mistaken for an old guest account.
4. Fail unauthenticated/mismatched-origin snapshot requests.
5. Confirm cached/static website routes never contain private data.
6. Exercise visibility toggle; anon users must not call RPCs.
7. Verify opted-out names disappear from public views after cache TTL.
