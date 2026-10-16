import Link from "next/link";
import { notFound } from "next/navigation";
import { hunterName } from "@/lib/hunterLicense";
import { getPublicHunter, isPublicHunterId } from "@/lib/publicHunters";

export const dynamic = "force-dynamic";
export const metadata = { title: "Hunter Career" };

export default async function PublicHunterPage({
  params,
}: { params: Promise<{ hunterId: string }> }) {
  const { hunterId } = await params;
  if (!isPublicHunterId(hunterId)) notFound();
  let hunter;
  try { hunter = await getPublicHunter(hunterId); }
  catch {
    return (
      <main className="page-shell career-page">
        <section className="panel maps-message">
          <h2>Public Hunter service unavailable</h2>
          <p>Career data could not be loaded right now.</p>
          <Link className="text-link" href="/hunters">Back to Hunters →</Link>
        </section>
      </main>
    );
  }
  // Private/opted-out profiles never resolve publicly, even by prior links.
  if (!hunter) notFound();
  return (
    <main className="page-shell career-page">
      <Link className="text-link maps-back" href="/hunters">← Public Hunters</Link>
      <section className="panel public-hunter-identity">
        <span className="eyebrow">PUBLIC HUNTER CAREER</span>
        <h1>{hunter.display_name}</h1>
        <p>Favorite Hunter · {hunterName(hunter.favorite_hunter)}</p>
        <p>Only lifetime, career-eligible statistics intentionally made public
          are displayed here.</p>
      </section>
      <section className="license-section">
        <div className="license-section-heading">
          <span className="eyebrow">TRUSTED CAREER TOTALS</span>
          <h2>Career statistics</h2>
        </div>
        <div className="license-stats">
          {[
            ["Matches", hunter.matches.toLocaleString("en-US")],
            ["Wins", hunter.wins.toLocaleString("en-US")],
            ["Losses", hunter.losses.toLocaleString("en-US")],
            ["Win Rate", hunter.win_rate.toFixed(1) + "%"],
            ["Kills", hunter.kills.toLocaleString("en-US")],
            ["Deaths", hunter.deaths.toLocaleString("en-US")],
            ["K/D", hunter.kd.toFixed(2)],
            ["Assists", hunter.assists.toLocaleString("en-US")],
            ["Headshots", hunter.headshots.toLocaleString("en-US")],
            ["Damage", hunter.damage.toLocaleString("en-US")],
            ["Octolith Scores", hunter.octolith_scores.toLocaleString("en-US")],
            ["Nodes Captured", hunter.nodes_captured.toLocaleString("en-US")],
          ].map(([label, value]) => (
            <div className="panel license-stat" key={label}>
              <span>{label}</span><strong>{value}</strong>
            </div>
          ))}
        </div>
      </section>
      <p className="helper">Personal match history, account identity, email and
        cosmetic inventory stay private even when a Hunter shares this page.</p>
      <Link className="button button-ghost" href="/leaderboards">View Leaderboards</Link>
    </main>
  );
}
