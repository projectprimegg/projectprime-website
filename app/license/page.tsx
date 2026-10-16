import Link from "next/link";
import { HunterLicenseDashboard } from "@/components/license/HunterLicenseDashboard";

export const metadata = {
  title: "Hunter License",
  description: "Your Project Prime Hunter License, accepted career results, recent matches and privacy controls.",
};
export const dynamic = "force-dynamic";

export default function HunterLicensePage() {
  return (
    <main className="page-shell license-page">
      <section className="page-hero license-hero">
        <span className="eyebrow">PROJECT PRIME // PLAYER IDENTITY</span>
        <h1>Your Hunter License.</h1>
        <p>The same verified career you see in the game, now accessible from
          the web. Sign in with the identity linked to Project Prime.</p>
        <div className="hero-actions">
          <Link className="button button-ghost" href="/hunters">Public Hunters</Link>
          <Link className="button button-ghost" href="/leaderboards">Leaderboards</Link>
        </div>
      </section>
      <HunterLicenseDashboard />
    </main>
  );
}
