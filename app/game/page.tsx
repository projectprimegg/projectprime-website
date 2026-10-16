export const metadata = { title: "Game" };

export default function GamePage() {
  return (
    <main className="page-shell">
      <section className="page-hero">
        <span className="eyebrow">ABOUT THE GAME</span>
        <h1>Metroid Prime Hunters, expanded.</h1>
        <p>Project Prime is focused on preserving the fast arena identity of Hunters while expanding what the client, maps and multiplayer systems can do.</p>
      </section>
      <section className="feature-grid">
        <article className="feature-card"><h3>Modern PC Client</h3><p>Built around Windows x64 with extensive controls, HUD customization and OpenGL-focused rendering.</p></article>
        <article className="feature-card"><h3>Expanded Multiplayer</h3><p>Classic play alongside new community modes, competitive rule sets and custom experiences.</p></article>
        <article className="feature-card"><h3>Custom Maps</h3><p>A growing map ecosystem designed for faithful ports, original arenas and new visual capabilities.</p></article>
        <article className="feature-card"><h3>Built to Grow</h3><p>The site and client architecture are being designed for profiles, leaderboards, tournaments, spectator features and replays.</p></article>
      </section>
    </main>
  );
}
