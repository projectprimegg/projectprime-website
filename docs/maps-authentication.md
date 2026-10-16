# Website Hunter License sign-in and map tickets

The website signs into the **same Supabase project as Project Prime**.
The browser uses only the public publishable key and supabase-js; never place
the service_role/secret key in website source or `NEXT_PUBLIC_*`.

Current game public defaults are used unless Vercel overrides:

- `NEXT_PUBLIC_SUPABASE_URL` (the project's HTTPS URL)
- `NEXT_PUBLIC_SUPABASE_PUBLISHABLE_KEY` (browser-safe public key)
- `NEXT_PUBLIC_MAP_SERVICE_URL` (the game Community map origin)

The website offers email sign-in and optional anonymous sign-in (when
enabled in the Supabase project **and** `NEXT_PUBLIC_ENABLE_GUEST_PUBLISHING=1`).
Guest publishing is disabled by default to avoid easy per-account quota evasion
through unlimited disposable browser identities. Enable only after rate limiting,
CAPTCHA/abuse protection and moderation are accepted. Supabase **Auth > URL Configuration** must set the **Site URL** to
`https://projectprime.gg` (not localhost), and allow the exact redirects
`https://projectprime.gg/license` and `https://projectprime.gg/maps/account`.
Website email links and OAuth always use these public URLs even from a local
or preview build. To test localhost callbacks explicitly, set
`NEXT_PUBLIC_HUNTER_AUTH_ORIGIN=http://localhost:3000` in a *local-only*
environment and allow the specific localhost paths in Supabase Auth.
Never deploy that variable to production. Email templates must honor
`{{ .ConfirmationURL }}` / `{{ .RedirectTo }}`, not hardcode `{{ .SiteURL }}`
or localhost. Already-issued verification emails retain their old links. The email provider and, optionally, anonymous
sign-ins must be enabled in the Supabase Auth dashboard.

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
