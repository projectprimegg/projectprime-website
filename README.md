# Project Prime Website

Official website for Project Prime.

## Current foundation

- Next.js App Router
- TypeScript
- Responsive Project Prime visual shell
- Home, Game, Download, Community
- Coming Soon pages for Maps, Hunters, Leaderboards, and Watch
- Automatic latest-release tracking from AntiNotAnti/Project-Prime
- Multi-platform downloads for Windows, Linux, macOS, and Android
- Device-aware recommended download highlighting
- Official Discord integration
- Vercel production deployment
- Custom domain: https://projectprime.gg

## Local development

```bash
npm install
npm run dev
```

Then open http://localhost:3000.

## Production

Production is deployed through Vercel from the GitHub repository.

Primary domain:

- https://projectprime.gg

The site auto-deploys when changes are pushed to the production branch connected in Vercel.

## Current integration points

- Official Discord: https://discord.gg/sudbyZMZh3
- Project Prime releases: https://github.com/AntiNotAnti/Project-Prime/releases/latest

## Next development priorities

- Polish the homepage with real Project Prime screenshots and gameplay media
- Improve mobile navigation
- Add installation instructions and troubleshooting
- Add richer release notes/changelog presentation
- Add legal / attribution content before public launch
- Prepare backend architecture for Hunter profiles, leaderboards, match history, replays, tournaments, and live viewing


## Deployment connection

Vercel Git integration is connected directly to `projectprimegg/projectprime-website`, so pushes to the production branch trigger deployments automatically.

Repository connection revalidated after the organization transfer.

Vercel Git connection re-established successfully on 2026-10-15.
