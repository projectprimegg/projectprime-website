import Link from "next/link";
import { browseHunterLeaderboard } from "@/lib/publicHunters";

export const metadata = {
  title: "Hunters",
  description: "Discover public-by-default Project Prime Hunter Licenses and accepted career statistics.",
};
export const dynamic = "force-dynamic";

type Search = { q?: string; page?: string };
function href(q: string, page: number) {
  const params = new URLSearchParams({ page: String(page) });
  if (q) params.set("q", q);
  return "/hunters?" + params.toString();
}

export default async function HuntersDirectory({
  searchParams,
}: { searchParams: Promise<Search> }) {
  const search = await searchParams;
  const q = (search.q || "").normalize("NFC").trim().slice(0, 40);
  const page = Math.max(1, Math.min(100, Number(search.page) || 1));
  let results;
  let unavailable = false;
  try {
    results = await browseHunterLeaderboard({
      search: q, page, onlyPublic: true, metric: "wins", mode: "all",
    });
  } catch { unavailable = true; }
  return (
    <main className="page-shell career-page">
      <section className="page-hero career-hero">
        <span className="eyebrow">HUNTER REGISTRY // PUBLIC PROFILES</span>
        <h1>Meet the Hunters.</h1>
        <p>Explore publicly visible Hunter Licenses, including new Hunters
          building their career. Players can opt out at any time.</p>
        <div className="hero-actions">
          <Link href="/license" className="button">My Hunter License</Link>
          <Link href="/leaderboards" className="button button-ghost">Leaderboards</Link>
        </div>
      </section>

      <form className="career-search panel" action="/hunters" method="get">
        <label htmlFor="hunter-search">Find a public Hunter</label>
        <div className="maps-filters">
          <input id="hunter-search" name="q" type="search" maxLength={40}
            placeholder="Search Hunter nickname" defaultValue={q} />
          <button className="button button-small" type="submit">Search</button>
        </div>
      </form>

      {unavailable ? (
        <section className="panel maps-message" role="status">
          <h3>Hunter directory unavailable</h3>
          <p>Public Hunter profiles could not be loaded. Your personal
            License remains accessible after sign-in.</p>
          <Link className="text-link" href="/hunters">Retry →</Link>
        </section>
      ) : results && <>
        <div className="career-title">
          <h2>Public Hunter profiles</h2>
          <span>{results.total} visible Hunters</span>
        </div>
        {results.entries.length === 0 ? (
          <section className="panel maps-message">
            <h3>No public Hunters found</h3>
            <p>Try another nickname. Hunters can opt out of public discovery
              from their License account settings.</p>
          </section>
        ) : <div className="public-hunters-grid">
          {results.entries.filter(item => item.is_public && item.public_id).map(item => (
            <article className="panel public-hunter-card" key={item.public_id}>
              <span className="mini-tag">PUBLIC HUNTER LICENSE</span>
              <h3><Link href={"/hunters/" + item.public_id}>{item.display_name}</Link></h3>
              <div className="public-hunter-metrics">
                <span>{item.matches.toLocaleString("en-US")} matches</span>
                <span>{item.wins.toLocaleString("en-US")} wins</span>
                <span>{item.kd.toFixed(2)} K/D</span>
              </div>
              <Link className="text-link" href={"/hunters/" + item.public_id}>
                View public career →
              </Link>
            </article>
          ))}
        </div>}
        <nav className="maps-pagination" aria-label="Hunter directory pages">
          {page > 1 ? <Link className="button button-ghost"
            href={href(q, page - 1)}>Previous</Link> : <span />}
          <span>Page {page} of {Math.max(1, Math.ceil(results.total / 25))}</span>
          {page < 100 && page * 25 < results.total ? <Link className="button button-ghost"
            href={href(q, page + 1)}>Next</Link> : <span />}
        </nav>
      </>}
      <p className="helper">Hunter Licenses are publicly discoverable by default.
        Players may opt out on the website or in-game, including guest Hunters.
        Email addresses, account UUIDs and private match history are never public.</p>
    </main>
  );
}
