export const metadata = { title: "Community" };

export default function CommunityPage() {
  return (
    <main className="page-shell">
      <section className="page-hero">
        <span className="eyebrow">COMMUNITY</span>
        <h1>Join Project Prime.</h1>
        <p>Discord will be the central place for players, development updates, map discussion, testing and tournament announcements.</p>
      </section>
      <section className="download-card panel">
        <div>
          <span className="mini-tag">DISCORD</span>
          <h2>Project Prime Community</h2>
          <p>The official invite will be connected here once you provide the permanent Discord invite URL.</p>
        </div>
        <button className="button disabled" disabled>Discord Link Coming Soon</button>
      </section>
    </main>
  );
}
