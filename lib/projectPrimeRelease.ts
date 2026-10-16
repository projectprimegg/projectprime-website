export type GitHubReleaseAsset = {
  name: string;
  browser_download_url: string;
  size: number;
  digest?: string | null;
};

export type GitHubRelease = {
  tag_name: string;
  name: string;
  html_url: string;
  published_at: string;
  body: string;
  assets: GitHubReleaseAsset[];
};

const LATEST_RELEASE_API =
  "https://api.github.com/repos/AntiNotAnti/Project-Prime/releases/latest";

export async function getLatestProjectPrimeRelease(): Promise<GitHubRelease> {
  const response = await fetch(LATEST_RELEASE_API, {
    headers: {
      Accept: "application/vnd.github+json",
      "X-GitHub-Api-Version": "2022-11-28",
    },
    next: { revalidate: 300 },
  });

  if (!response.ok) {
    throw new Error(`GitHub latest release request failed: ${response.status}`);
  }

  return response.json();
}

export function getWindowsClientAsset(release: GitHubRelease) {
  return release.assets.find(
    (asset) =>
      /-win-x64\.zip$/i.test(asset.name) &&
      !/-server-win-x64\.zip$/i.test(asset.name),
  );
}

export function formatBytes(bytes: number) {
  const mb = bytes / 1024 / 1024;
  return `${mb.toFixed(1)} MB`;
}
