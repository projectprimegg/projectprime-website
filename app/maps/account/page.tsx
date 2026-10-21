import Link from "next/link";
import { MapAccountPanel } from "@/components/maps/MapAccount";
import { AuthReturnNotice } from "@/components/license/AuthReturnNotice";

export const metadata = { title: "Map Creator Access" };

export default function AccountPage() {
  return (
    <main className="page-shell maps-library">
      <section className="page-hero maps-hero">
        <span className="eyebrow">HUNTER LICENSE</span>
        <h1>Creator access.</h1>
        <p>Publish without registering, or sign in with your Hunter License
          to keep map ownership across devices.</p>
        <Link className="text-link" href="/maps">← Map Library</Link>
      </section>
      <AuthReturnNotice />
      <MapAccountPanel />
    </main>
  );
}
