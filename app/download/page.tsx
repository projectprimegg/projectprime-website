import Link from "next/link";

export const metadata = { title: "Download" };

export default function DownloadPage() {
  return (
    <main className="page-shell">
      <section className="page-hero">
        <span className="eyebrow">GET PROJECT PRIME</span>
        <h1>Download the client.</h1>
        <p>The official Project Prime download area is ready for the release pipeline. The live client package will be connected here before public launch.</p>
      </section>
      <section className="download-card panel">
        <div>
          <span className="mini-tag">WINDOWS x64</span>
          <h2>Project Prime Client</h2>
          <p>Latest public release information, changelog, checksum and installer download will appear here.</p>
        </div>
        <button className="button disabled" disabled>Download Coming Soon</button>
      </section>
      <p className="helper">Need help when downloads go live? Installation instructions and troubleshooting will be published on this page.</p>
      <Link className="text-link" href="/community">Join the community for development updates →</Link>
    </main>
  );
}
