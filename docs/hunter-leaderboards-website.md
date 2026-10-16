# Website Hunter career leaderboards and public profiles

The website consumes the read-only Supabase `hunter-leaderboard` Edge
Function implemented in AntiNotAnti/Project-Prime#624. This is not a second
career ingestion pipeline, and no browser can write wins or rating.

## Pages

- `/leaderboards`: server-rendered, paginated lifetime leaderboard.
  Whitelisted sorts: wins, kills, matches, K/D, win rate and headshots.
  Filters use the exact numeric native GameMode IDs.
- `/hunters`: directory of only those Hunter profiles whose owners
  explicitly opted in, including nickname search.
- `/hunters/{opaqueId}`: opted-in public career summary only.
- `/license`: signed-in owner's private Hunter License and visibility
  preference. Existing game account identity remains authoritative.

Private players may still contribute anonymous competition scores/ranks,
but the server sends no name, auth account UUID or public profile slug for
them. The website additionally masks any such fields from malformed
backend rows. The directory search cannot reveal private profiles.

Rated matchmaking has no accepted rating transactions yet. The
leaderboard explicitly labels its results as **career** statistics, not
a ranked-rating or seasonal leaderboard. Do not synthesize an RP score
from wins/kills.

## Configuration

Website uses the existing `NEXT_PUBLIC_SUPABASE_URL` and
`NEXT_PUBLIC_SUPABASE_PUBLISHABLE_KEY` from the map creator account.
The server-side public ranking fetch uses that publishable key only.
Never expose `SUPABASE_SERVICE_ROLE_KEY` or `SUPABASE_DB_URL`.

Authenticated snapshot requests use POST /api/license/snapshot;
they are private no-store responses, unlike public cached ranking pages.
Public Edge reads have a 60-second cache TTL, so opting out may take
up to one TTL to clear cached public pages.

## Release checklist

1. Apply backend privacy migration and run RLS/RPC permission tests.
2. Deploy Supabase read-only `hunter-leaderboard` function.
3. Merge website Hunter License PR before this PR, then release website.
4. Verify real game-linked Hunter profile and private match history.
5. Verify opted-out users show anonymous ranks, no public page.
6. Opt in, check a searchable public name and limited profile.
7. Opt out, verify 404 on old link after cache TTL.
8. Verify mode filtering, pagination and empty rankings.
9. Check anonymous/profile API never returns an email, auth UUID,
   accepted raw match report or private recent-match payload.
10. Verify bot-assisted matches and local match metrics do not affect
    the trusted global career projection.
