# Website Hunter License sign-in and map tickets

The website signs into the **same Supabase project as Project Prime**.
The browser uses only the public publishable key and supabase-js; never place
the service_role/secret key in website source or `NEXT_PUBLIC_*`.

Current game public defaults are used unless Vercel overrides:

- `NEXT_PUBLIC_SUPABASE_URL` (the project's HTTPS URL)
- `NEXT_PUBLIC_SUPABASE_PUBLISHABLE_KEY` (browser-safe public key)
- `NEXT_PUBLIC_MAP_SERVICE_URL` (the game Community map origin)

The website supports creator accounts and account-free guest map uploads.
On `/maps/upload`, visitors can inspect either file format before logging in.
On explicit submission the browser checks for a current session and calls
Supabase `signInAnonymously()` only if necessary. The guest receives a
browser-bound Supabase identity with a short-lived `ppm1` Community ticket.
This is **not** a public or shared upload token. The `/maps/account` and
standalone PK3 pages also offer **Continue as Guest**. The previous
`NEXT_PUBLIC_ENABLE_GUEST_PUBLISHING` UI flag is no longer needed.

**Security:** Disposable anonymous identities can circumvent per-creator quotas.
Keep Supabase anonymous signup rate limits enabled, and configure
reverse-proxy/WAF upload throttles, moderation and storage quotas. A future
CAPTCHA/Turnstile deployment must pass a provider token to Supabase anonymous
sign-in first; simply requiring CAPTCHA in Auth without a browser token
will block guest uploads. Never put service-role keys in browser code.

Supabase **Auth > URL Configuration** must set the **Site URL** to
`https://projectprime.gg` (not localhost), and allow the exact redirects
`https://projectprime.gg/license` and `https://projectprime.gg/maps/account`.
Website email links and OAuth always use these public URLs even from a local
or preview build. To test localhost callbacks explicitly, set
`NEXT_PUBLIC_HUNTER_AUTH_ORIGIN=http://localhost:3000` in a *local-only*
environment and allow the specific localhost paths in Supabase Auth.
Never deploy that variable to production. Email templates must honor
`{{ .ConfirmationURL }}` / `{{ .RedirectTo }}`, not hardcode `{{ .SiteURL }}`
or localhost. Already-issued verification emails retain their old links. The email provider and anonymous sign-ins must be enabled in the Supabase Auth dashboard for both modes to work.

`POST /api/maps/ticket` accepts the user's Supabase access JWT over
same-origin HTTPS, forwards it to the already-existing Supabase Edge Function
`community-map-ticket`, and returns the function's 15-minute `ppm1`
ticket with `Cache-Control: no-store`. The map service validates that
ticket and derives owner permissions on every protected map API request.
No arbitrary user ID is accepted from the website; the browser does not
receive a master uploader token.

A browser guest account and an existing game guest account are **distinct**
unless the user links both clients to the same permanent identity. Do not
claim guest ownership automatically travels across devices.

The website does not implement its own map storage or publishing tickets.
No database schema or RLS changes are necessary for this integration.

## Security acceptance

- Missing/expired JWTs cannot mint a ticket; denied responses are not cached.
- Third-party website origins cannot mint tickets through the same-origin route.
- Permanent Supabase credentials and service-role secrets never reach the client.
- Publishing authorizations and revisions remain enforced by the map service.
- Sign in/out and email-link return work with the production redirect allowlist.
