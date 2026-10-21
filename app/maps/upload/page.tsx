import Link from "next/link";
import { MapUpload } from "@/components/maps/MapUpload";

export const metadata = { title: "Publish Community Map" };

export default function UploadMapPage() {
  return (
    <main className="page-shell maps-library">
      <section className="page-hero maps-hero">
        <span className="eyebrow">MAP CREATOR // PUBLISH</span>
        <h1>Share a battlefield.</h1>
        <p>Upload a Project Prime .ppmap or Quake .pk3 using the same file picker.
          PK3 archives are converted and verified before publishing to Community.
          No Hunter License registration is required.</p>
        <Link href="#map-upload" className="button button-ghost">
          Upload .ppmap or .pk3 ↓
        </Link>
        <Link className="text-link" href="/maps">← Map Library</Link>
      </section>
      <MapUpload />
    </main>
  );
}
