import Link from "next/link";
import { PlatformDownloads } from "@/components/PlatformDownloads";
import {
  formatBytes,
  getClientAsset,
  getLatestProjectPrimeRelease,
} from "@/lib/projectPrimeRelease";

export const metadata = { title: "Download" };

const platforms = [
  { key: "windows", label: "Windows", detail: "Windows x64", button: "Download for Windows" },
  { key: "linux", label: "Linux", detail: "Linux x64", button: "Download for Linux" },
  { key: "macos-arm64", label: "macOS", detail: "Apple Silicon", button: "Download for Apple Silicon" },
  { key: "macos-x64", label: "macOS", detail: "Intel Mac", button: "Download for Intel Mac" },
  { key: "android", label: "Android", detail: "APK", button: "Download for Android" },
] as const;

export default async function DownloadPage() {
  let release;
  try {
    release = await getLatestProjectPrimeRelease();
  } catch {
    release = null;
  }

  const releaseDate = release
    ? new Intl.DateTimeFormat("en-US", { year: "numeric", month: "long", day: "numeric" }).format(new Date(release.published_at))
    : null;

  const options = platforms.map((platform) => {
    const asset = release ? getClientAsset(release, platform.key) : undefined;
    return {
      ...platform,
      filename: asset?.name,
      size: asset ? formatBytes(asset.size) : undefined,
      url: asset?.browser_download_url,
    };
  });

  return (
    <main className="page-shell">
      <section className="page-hero">
        <span className="eyebrow">GET PROJECT PRIME</span>
        <h1>Choose your platform.</h1>
        <p>
          Project Prime highlights the build that best matches your current device,
          but every available operating system remains visible and selectable.
        </p>
      </section>

      {release ? (
        <section className="panel" style={{ marginBottom: "1rem" }}>
          <span className="mini-tag">LATEST RELEASE</span>
          <h2>{release.name}</h2>
          <p><strong>{release.tag_name}</strong>{releaseDate ? <> · Published {releaseDate}</> : null}</p>
        </section>
      ) : null}

      <PlatformDownloads
        options={options}
        fallbackUrl="https://github.com/AntiNotAnti/Project-Prime/releases/latest"
      />

      <section className="panel" style={{ marginTop: "1rem" }}>
        <span className="mini-tag">OFFICIAL RELEASE CHANNEL</span>
        <h2>Always current.</h2>
        <p>
          This page reads the latest non-draft, non-prerelease GitHub release from
          AntiNotAnti/Project-Prime and lists the normal client builds for Windows,
          Linux, macOS, and Android while excluding dedicated-server packages.
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
        Project Prime does not include game data. Players provide their own Metroid Prime Hunters cartridge dump.
      </p>
      <Link className="text-link" href="/community">Join the community for development updates →</Link>
    </main>
  );
}
