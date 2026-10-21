import Link from "next/link";
import { Pk3Upload } from "@/components/maps/Pk3Upload";

export const metadata = {
  title: "Convert and Publish Quake PK3",
  description: "Upload, convert and publish a Quake III .pk3 to Project Prime Community maps.",
};

export default function Pk3PublishPage() {
  return (
    <main className="page-shell maps-library">
      <section className="page-hero maps-hero">
        <span className="eyebrow">COMMUNITY CREATOR // QUAKE INTERCHANGE</span>
        <h1>Bring a Quake arena to Prime.</h1>
        <p>Direct .pk3 upload, dedicated conversion, verification and Community publication
          as a guest or registered creator.</p>
        <div className="hero-actions">
          <Link href="/maps/upload" className="button button-ghost">← Standard .ppmap Upload</Link>
          <Link href="/maps" className="button button-ghost">Browse Maps</Link>
        </div>
      </section>
      <Pk3Upload />
    </main>
  );
}
