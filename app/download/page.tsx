import Link from "next/link";
import {
  formatBytes,
  getLatestProjectPrimeRelease,
  getWindowsClientAsset,
} from "@/lib/projectPrimeRelease";

export const metadata = { title: "Download" };

export default async function DownloadPage() {
  let release;
  try {
    release = await getLatestProjectPrimeRelease();
  } catch {
    release = null;
  }

  const windowsAsset = release ? getWindowsClientAsset(release) : undefined;
  const releaseDate = release
    ? new Intl.DateTimeFormat("en-US", {
        year: "numeric",
        month: "long",
        day: "numeric",
      }).format(new Date(release.published_at))
    : null;

  return (
    <main className="page-shell">
      <section className="page-hero">
        <span className="eyebrow">GET PROJECT PRIME</span>
        <h1>Download the client.</h1>
        <p>
          The download below always tracks the latest public Project Prime release.
          When a new release is published on the official GitHub repository, this
          page updates automatically.
        </p>
      </section>

      <section className="download-card panel">
        <div>
          <span className="mini-tag">WINDOWS x64</span>
          <h2>{release?.name ?? "Project Prime Client"}</h2>
          {release ? (
            <>
              <p>
                Latest stable release: <strong>{release.tag_name}</strong>
                {releaseDate ? <> · Published {releaseDate}</> : null}
                {windowsAsset ? <> · {formatBytes(windowsAsset.size)}</> : null}
              </p>
              {windowsAsset?.digest ? (
                <p className="helper">Checksum: {windowsAsset.digest}</p>
              ) : null}
            </>
          ) : (
            <p>
              Live release information is temporarily unavailable. The download
              button will fall back to the official latest-release page.
            </p>
          )}
        </div>

        <a className="button" href="/api/download/latest/windows">
          Download Latest for Windows
        </a>
      </section>

      <section className="panel" style={{ marginTop: "1rem" }}>
        <span className="mini-tag">OFFICIAL RELEASE CHANNEL</span>
        <h2>Always current.</h2>
        <p>
          Project Prime reads the latest non-draft, non-prerelease GitHub release
          from AntiNotAnti/Project-Prime and selects the desktop Windows x64 build,
          excluding dedicated-server packages.
        </p>
        <a
          className="text-link"
          href={release?.html_url ?? "https://github.com/AntiNotAnti/Project-Prime/releases/latest"}
          target="_blank"
          rel="noreferrer"
        >
          View release notes on GitHub →
        </a>
      </section>

      <p className="helper">
        Project Prime does not include game data. Players provide their own
        Metroid Prime Hunters cartridge dump.
      </p>
      <Link className="text-link" href="/community">
        Join the community for development updates →
      </Link>
    </main>
  );
}
