import Link from "next/link";

const future = [
  ["Hunter Profiles", "Search players, view Hunter Licenses, career stats and match history."],
  ["Leaderboards", "Competitive rankings and deep statistics across supported game modes."],
  ["Maps", "Discover curated map packs and community-created battlefields."],
  ["Watch", "Live tournaments, broadcasts, spectator tools and future match replays."],
];

export default function Home() {
  return (
    <main>
      <section className="hero">
        <div className="hero-grid" />
        <div className="hero-content">
          <span className="eyebrow">THE HUNT CONTINUES</span>
          <h1>PROJECT <span>PRIME</span></h1>
          <p className="hero-copy">
            A community-driven evolution of Metroid Prime Hunters built for modern PC play,
            expanded multiplayer, custom maps and a growing competitive community.
          </p>
          <div className="hero-actions">
            <Link className="button" href="/download">Download Project Prime</Link>
            <Link className="button button-ghost" href="/community">Join the Community</Link>
          </div>
        </div>
        <div className="hero-status">
          <span className="status-dot" />
          <span>PROJECT PRIME // ACTIVE DEVELOPMENT</span>
        </div>
      </section>

      <section className="section intro">
        <span className="eyebrow">WELCOME TO PROJECT PRIME</span>
        <h2>Built to push Hunters further.</h2>
        <p>
          Project Prime expands the classic arena experience with modern client features,
          community maps, new multiplayer modes, competitive tools and a platform designed
          to keep growing.
        </p>
        <div className="stat-strip">
          <div><strong>PC</strong><span>Windows x64</span></div>
          <div><strong>ONLINE</strong><span>Multiplayer focused</span></div>
          <div><strong>OPENGL</strong><span>Primary renderer</span></div>
          <div><strong>COMMUNITY</strong><span>Built to evolve</span></div>
        </div>
      </section>

      <section className="section">
        <div className="section-heading">
          <div>
            <span className="eyebrow">EXPLORE</span>
            <h2>The Project Prime platform</h2>
          </div>
          <p>Today is the foundation. These areas are being designed now so they can grow into the full Project Prime ecosystem later.</p>
        </div>
        <div className="feature-grid">
          {future.map(([title, description]) => (
            <article className="feature-card" key={title}>
              <span className="mini-tag">COMING SOON</span>
              <h3>{title}</h3>
              <p>{description}</p>
            </article>
          ))}
        </div>
      </section>

      <section className="section cta panel">
        <div>
          <span className="eyebrow">READY TO PLAY?</span>
          <h2>Enter the arena.</h2>
          <p>Get the latest Project Prime client and join the community.</p>
        </div>
        <div className="hero-actions">
          <Link className="button" href="/download">Download</Link>
          <Link className="button button-ghost" href="/community">Discord</Link>
        </div>
      </section>
    </main>
  );
}
