export const metadata = { title: "Community" };

export default function CommunityPage() {
  return (
    <main className="page-shell">
      <section className="page-hero">
        <span className="eyebrow">COMMUNITY</span>
        <h1>Join Project Prime.</h1>
        <p>Discord is the central place for players, development updates, map discussion, testing and tournament announcements.</p>
      </section>
      <section className="download-card panel">
        <div>
          <span className="mini-tag">DISCORD</span>
          <h2>Project Prime Community</h2>
          <p>Join the official Project Prime Discord to find players, follow development, discuss maps and game modes, and keep up with community events.</p>
        </div>
        <a
          className="button"
          href="https://discord.gg/sudbyZMZh3"
          target="_blank"
          rel="noreferrer"
        >
          Join Discord
        </a>
      </section>
    </main>
  );
}
