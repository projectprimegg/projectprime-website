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
        <Link className="text-link" href="/maps">← Map Library</Link>
      </section>
      <MapUpload />
    </main>
  );
}
