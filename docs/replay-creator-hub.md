# Replay Creator Hub (Watch)

This is a **link-sharing** workflow. It is not a hosted replay catalog,
game recording upload, authenticated creator account or backend settlement.

- `/watch`: Watch landing with a real creator route
- `/watch/creator`: users export a local replay to video in Crescent's Prime Studio,
  host that video on YouTube (public or unlisted), then paste the hosted URL
- `/watch/replays?video=<11-character-id>&title=<optional-text>`:
  shareable, independent page embedding only `youtube-nocookie.com`
- `lib/replayShare.ts`: strict HTTPS YouTube/youtu.be host allowlist, bounded
  video ID, bounded title, no arbitrary origins or raw file paths in generated links
- No backend persistence, no uploads, no local filesystem APIs, no private
  Hunter License data and no claims that a shared video is verified game evidence
- Someone who knows the share URL can view the hosted video, subject to
  YouTube's own visibility settings and availability.
- Deploy the website route **before** merging Crescent's in-game Creator Hub
  action, which opens only the fixed official URL.

Validate with `npm ci && npm run test:replays && npx tsc --noEmit && npm run build`.
