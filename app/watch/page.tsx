import Link from "next/link";
export const metadata = {title:"Watch & Replays",description:"Official videos and creator-shared replay highlight links."};
export default function WatchPage() {
  return <main className="page-shell replay-watch">
    <section className="page-hero">
      <span className="eyebrow">PROJECT PRIME // THEATRE</span>
      <h1>Great matches deserve a replay.</h1>
      <p>Browse official gameplay videos or turn an exported Prime Studio clip
        into a shareable creator link. Live broadcasts and a hosted replay
        catalog are planned, not available here yet.</p>
      <div className="hero-actions">
        <Link className="button" href="/watch/creator">Replay creator hub</Link>
        <Link className="button button-ghost" href="/media">Official videos</Link>
      </div>
    </section>
    <div className="replay-watch-grid">
      <article className="panel replay-watch-card">
        <span className="mini-tag">WATCH</span><h2>Replay Library</h2>
        <p>In Crescent, pick a saved recording and select Watch to inspect the match.</p>
      </article>
      <article className="panel replay-watch-card">
        <span className="mini-tag">PRIME STUDIO</span><h2>Make a highlight</h2>
        <p>Set a clip range, add camera moves, export video, and share the hosted result.</p>
        <Link className="text-link" href="/watch/creator">Creator guide</Link>
      </article>
    </div>
  </main>;
}
