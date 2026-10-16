# Website Hunter License and public visibility

The /license page uses the same Supabase Auth identity as Project Prime and
Community map publishing. It does not create a second career database.

## Account and private data

The private `POST /api/license/snapshot` relay validates the current signed-in
Supabase session through the existing Hunter License Edge Function, returning
only that user's full career snapshot, 25 recent accepted matches and personal
cosmetic loadouts. The website does not cache this response or expose email,
authentication tokens or detailed match logs to anonymous visitors.

Sign-in supports passwordless email, linked password or configured
Google/GitHub/Discord providers. A fresh website identity does not
automatically take ownership of an unrelated in-game guest license.

## Public-by-default profile and owner opt-out

By default, both newly-created and existing Hunter Licenses without a stored
preference expose their in-game nickname, favorite Hunter and limited lifetime
career statistics in the public directory and profile card. The change is
**retroactive for accounts with no saved preference**. Profiles with zero
eligible matches may still appear in the directory with zeroed totals.

The signed-in owner can turn the public profile switch **off** to opt out on
`/license`. Before backend activation, the website also offers
**Save My Private Opt-Out** while profiles are still private; this creates
an explicit `false` choice so existing accounts do not become public
during the migration. The in-game Hunter License Account screen likewise
offers **Save Private Preference** for anonymous/guest Hunters without
a website recovery identity. A saved `is_public=false` is durable and
never changed by a migration.
Opted-out users appear as "Private Hunter" in authoritative leaderboards,
with their name/public ID hidden; private match history stays private.

The website invokes only these authenticated owner-scoped Supabase RPCs:

- `project_prime_hunter_visibility()`
- `project_prime_set_hunter_visibility(p_public boolean)`

Neither accepts a player UUID. They cannot edit career counters or another
Hunter's visibility. The visible toggle is disabled when the RPC cannot
confirm state, rather than inventing a default on a failed connection.

Public directory, leaderboard and public profile requests use `no-store`
so a new request reflects an opt-out without a server cache delay. Browser
pages already viewed elsewhere cannot be remotely erased retroactively.

## Deployment order

1. Release the game UI opt-out, especially for guest/anonymous accounts.
2. Merge and deploy the website public-by-default copy.
3. Apply `20261016214500_hunter_public_default_opt_out.sql` and deploy the
   matching `hunter-leaderboard` Edge Function on the **same** Supabase project.
4. Verify the RPC retains false overrides and has no anon grants.
5. Test a newly public profile with zero matches, a name search, anonymous
   leaderboard masking, opt-out (old profile URL returns 404), and opt-in.
6. Verify emails, real auth UUIDs, raw match evidence and private match logs
   never appear in public responses.

Do not silently enable public presentation on live backend before players
can access the opt-out controls. The database/Edge rollout will change the
visibility of existing accounts without saved preferences.
