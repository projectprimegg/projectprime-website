import Link from "next/link";
import { MapCreatorDashboard } from "@/components/maps/MapCreatorDashboard";

export const metadata = { title: "My Community Maps" };

export default function MyMapsPage() {
  return (
    <main className="page-shell maps-library">
      <section className="page-hero maps-hero">
        <span className="eyebrow">CREATOR DASHBOARD</span>
        <h1>My Maps.</h1>
        <p>Publish new maps, inspect immutable revision history, or choose
          which version players discover in-game.</p>
        <Link className="text-link" href="/maps">← Map Library</Link>
      </section>
      <MapCreatorDashboard />
    </main>
  );
}
