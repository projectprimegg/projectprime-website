import Link from "next/link";
import { MapUpload } from "@/components/maps/MapUpload";

export const metadata = { title: "Publish Community Map" };

export default function UploadMapPage() {
  return (
    <main className="page-shell maps-library">
      <section className="page-hero maps-hero">
        <span className="eyebrow">MAP CREATOR // PUBLISH</span>
        <h1>Share a battlefield.</h1>
        <p>Select up to 30 mixed Project Prime .ppmap and Quake .pk3 archives.
          Uploads are tracked individually, and PK3 conversions require review
          before they can be published. No Hunter License registration is required.</p>
        <Link href="#map-upload" className="button button-ghost">
          Bulk upload maps ↓
        </Link>
        <Link className="text-link" href="/maps">← Map Library</Link>
      </section>
      <MapUpload />
    </main>
  );
}
