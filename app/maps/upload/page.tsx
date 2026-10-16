import Link from "next/link";
import { MapUpload } from "@/components/maps/MapUpload";

export const metadata = { title: "Publish Community Map" };

export default function UploadMapPage() {
  return (
    <main className="page-shell maps-library">
      <section className="page-hero maps-hero">
        <span className="eyebrow">MAP CREATOR // PUBLISH</span>
        <h1>Share a battlefield.</h1>
        <p>Upload directly to the Project Prime Community service. No new
          game release is needed to distribute published maps.</p>
        <p className="helper">Have a compiled Quake .pk3? Use the dedicated
          conversion service to build a compatible Project Prime package.</p>
        <Link href="/maps/upload/pk3" className="button button-ghost">
          Convert Quake .pk3 →
        </Link>
        <Link className="text-link" href="/maps">← Map Library</Link>
      </section>
      <MapUpload />
    </main>
  );
}
