import Link from "next/link";
import {
  BOARD_METRICS, BOARD_MODES, boardScore, browseHunterLeaderboard,
  isBoardMetric, isBoardMode,
} from "@/lib/publicHunters";

export const metadata = {
  title: "Leaderboards",
  description: "Authoritative lifetime Project Prime Hunter career rankings for accepted online matches.",
};
export const dynamic = "force-dynamic";

type Query = { metric?: string; mode?: string; page?: string };
function href(query: Query, page: number) {
  const p = new URLSearchParams();
  p.set("metric", query.metric || "wins");
  p.set("mode", query.mode || "all");
  p.set("page", String(page));
  return "/leaderboards?" + p.toString();
}

export default async function LeaderboardsPage({
  searchParams,
}: { searchParams: Promise<Query> }) {
  const search = await searchParams;
  const metric = isBoardMetric(search.metric) ? search.metric : "wins";
  const mode = isBoardMode(search.mode) ? search.mode : "all";
  const page = Math.max(1, Math.min(100, Number(search.page) || 1));
  let leaderboard;
  let unavailable = false;
  try {
    leaderboard = await browseHunterLeaderboard({ metric, mode, page });
  } catch { unavailable = true; }

  const selected = BOARD_METRICS.find(item => item.value === metric)?.label || "Wins";
  return (
    <main className="page-shell career-page">
      <section className="page-hero career-hero">
        <span className="eyebrow">PROJECT PRIME // COMPETITIVE RECORD</span>
        <h1>Hunter Leaderboards.</h1>
        <p>Standings built from server-accepted, career-eligible Project Prime
          multiplayer matches. Every entry comes from the game&apos;s authoritative records.</p>
        <div className="hero-actions">
          <Link className="button button-ghost" href="/license">My Hunter License</Link>
          <Link className="button button-ghost" href="/hunters">Find Hunters</Link>
        </div>
      </section>

      <form className="career-filters panel" action="/leaderboards" method="get">
        <label htmlFor="career-sort">Rank by</label>
        <select id="career-sort" name="metric" defaultValue={metric}>
          {BOARD_METRICS.map(item => <option key={item.value} value={item.value}>
            {item.label}
          </option>)}
        </select>
        <label htmlFor="career-mode">Game mode</label>
        <select id="career-mode" name="mode" defaultValue={mode}>
          {BOARD_MODES.map(item => <option key={item.value} value={item.value}>
            {item.label}
          </option>)}
        </select>
        <button type="submit" className="button button-small">Apply Filters</button>
      </form>

      <div className="career-title">
        <div>
          <span className="eyebrow">LIFETIME CAREER</span>
          <h2>{selected} standings</h2>
        </div>
        {!unavailable && leaderboard && <span>{leaderboard.total} eligible Hunters</span>}
      </div>

      {unavailable ? (
        <section className="panel maps-message" role="status">
          <h3>Career leaderboard is unavailable.</h3>
          <p>The public career service has not been deployed or could not be reached.
            No unverified or fabricated rankings are shown.</p>
          <Link href="/leaderboards" className="text-link">Try again →</Link>
        </section>
      ) : leaderboard && <>
        {!leaderboard.ranked_available && (
          <p className="career-notice">
            Competitive rating has no accepted ranked results yet. These are
            verified lifetime career standings, not a ranked-rating or seasonal ladder.
          </p>
        )}
        <div className="career-table-wrap panel">
          <table className="career-table">
            <thead>
              <tr>
                <th scope="col">Rank</th>
                <th scope="col">Hunter</th>
                <th scope="col">{selected}</th>
                <th scope="col">Matches</th>
                <th scope="col">Wins</th>
                <th scope="col">Kills</th>
                <th scope="col">K/D</th>
                <th scope="col">Win Rate</th>
              </tr>
            </thead>
            <tbody>
              {leaderboard.entries.map((entry) => (
                <tr key={entry.rank}>
                  <td className="career-position">#{entry.rank}</td>
                  <td>
                    {entry.is_public && entry.public_id
                      ? <Link className="career-public-link" href={"/hunters/" + entry.public_id}>
                          {entry.display_name}
                        </Link>
                      : <span className="career-private-name">Private Hunter</span>}
                    {!entry.is_public && <span className="career-private-label">Identity hidden</span>}
                  </td>
                  <td className="career-score">{boardScore(entry, metric)}</td>
                  <td>{entry.matches.toLocaleString("en-US")}</td>
                  <td>{entry.wins.toLocaleString("en-US")}</td>
                  <td>{entry.kills.toLocaleString("en-US")}</td>
                  <td>{entry.kd.toFixed(2)}</td>
                  <td>{entry.win_rate.toFixed(1)}%</td>
                </tr>
              ))}
            </tbody>
          </table>
          {leaderboard.entries.length === 0 &&
            <p className="career-empty">No eligible matches have been recorded for this selection.</p>}
        </div>
        <nav className="maps-pagination" aria-label="Leaderboard pages">
          {page > 1 ? <Link className="button button-ghost"
            href={href({ metric, mode }, page - 1)}>Previous</Link> : <span />}
          <span>Page {page} of {Math.max(1, Math.ceil(leaderboard.total / 25))}</span>
          {page < 100 && page * 25 < leaderboard.total
            ? <Link className="button button-ghost"
              href={href({ metric, mode }, page + 1)}>Next</Link> : <span />}
        </nav>
      </>}
      <p className="helper career-disclosure">Hunter names and limited public
        profiles appear by default. Players can opt out from their License
        account screen in-game or on the website; opted-out entries retain
        anonymous career positions without exposing their identity.</p>
    </main>
  );
}
