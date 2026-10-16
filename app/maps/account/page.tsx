import Link from "next/link";
import { MapAccountPanel } from "@/components/maps/MapAccount";

export const metadata = { title: "Hunter Sign In" };

export default function AccountPage() {
  return (
    <main className="page-shell maps-library">
      <section className="page-hero maps-hero">
        <span className="eyebrow">HUNTER LICENSE</span>
        <h1>Creator account.</h1>
        <p>Sign in to publish maps and manage your revisions.</p>
        <Link className="text-link" href="/maps">← Map Library</Link>
      </section>
      <MapAccountPanel />
    </main>
  );
}
