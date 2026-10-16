import Link from "next/link";
import { getVideoMetadata, projectPrimeVideos } from "@/lib/videos";

const future = [
  ["Hunter License", "View your authenticated in-game career statistics, match history and progression.", "/license", true],
  ["Leaderboards", "Browse server-accepted lifetime career rankings by mode and achievement.", "/leaderboards", true],
  ["Maps", "Discover and publish custom community arenas.", "/maps", true],
  ["Hunter Directory", "Explore the public profiles of Hunters who opted in.", "/hunters", true],
  ["Watch", "Live tournaments, broadcasts and future match replays.", "/watch", false],
] as const;

export default async function Home() {
  const latestVideos = await Promise.all(
    [...projectPrimeVideos].slice(-3).reverse().map(getVideoMetadata),
  );

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
            <a className="button button-ghost" href="#trailer">Watch Trailer</a>
            <Link className="button button-ghost" href="/community">Join the Community</Link>
          </div>
        </div>
        <div className="hero-status">
          <span className="status-dot" />
          <span>PROJECT PRIME // ACTIVE DEVELOPMENT</span>
        </div>
      </section>

      <section className="section trailer-section" id="trailer">
        <div className="section-heading">
          <div>
            <span className="eyebrow">FEATURED TRAILER</span>
            <h2>Watch Project Prime.</h2>
          </div>
          <p>This is the current featured trailer while a dedicated official Project Prime trailer is in production.</p>
        </div>
        <div className="featured-trailer panel">
          <div className="video-frame featured-video-frame">
            <iframe
              src="https://www.youtube-nocookie.com/embed/zQsoELTXcvI"
              title="Project Prime featured trailer"
              loading="lazy"
              allow="accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture; web-share"
              allowFullScreen
            />
          </div>
          <div className="featured-trailer-copy">
            <span className="mini-tag">CURRENT TRAILER</span>
            <p>A temporary featured showcase until the full official Project Prime trailer is released.</p>
            <a className="text-link" href="https://youtu.be/zQsoELTXcvI" target="_blank" rel="noreferrer">Watch on YouTube →</a>
          </div>
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
            <span className="eyebrow">LATEST VIDEOS</span>
            <h2>See Project Prime in action.</h2>
          </div>
          <p>Development showcases and gameplay from the current Project Prime release cycle.</p>
        </div>
        <div className="video-grid video-grid-home">
          {latestVideos.map((video) => (
            <article className="video-card panel" key={video.id}>
              <div className="video-frame">
                <iframe
                  src={`https://www.youtube-nocookie.com/embed/${video.id}`}
                  title={video.title}
                  loading="lazy"
                  allow="accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture; web-share"
                  allowFullScreen
                />
              </div>
              <div className="video-card-copy">
                <span className="mini-tag">YOUTUBE</span>
                <h3>{video.title}</h3>
              </div>
            </article>
          ))}
        </div>
        <Link className="text-link" href="/media">View all Project Prime videos →</Link>
      </section>

      <section className="section">
        <div className="section-heading">
          <div>
            <span className="eyebrow">EXPLORE</span>
            <h2>The Project Prime platform</h2>
          </div>
          <p>Explore connected services across the Project Prime platform. Additional features continue to grow.</p>
        </div>
        <div className="feature-grid">
          {future.map(([title, description, href, live]) => (
            <article className="feature-card" key={title}>
              <span className="mini-tag">{live ? "AVAILABLE" : "COMING SOON"}</span>
              <h3><Link href={href}>{title}</Link></h3>
              <p>{description}</p>
              {live && <Link className="text-link" href={href}>Explore →</Link>}
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
