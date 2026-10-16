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

export function getClientAsset(
  release: GitHubRelease,
  platform: "windows" | "linux" | "macos-arm64" | "macos-x64" | "android",
) {
  const matchers = {
    windows: /-win-x64\.zip$/i,
    linux: /-linux-x64\.tar\.gz$/i,
    "macos-arm64": /-osx-arm64\.tar\.gz$/i,
    "macos-x64": /-osx-x64\.tar\.gz$/i,
    android: /-android\.apk$/i,
  } as const;

  return release.assets.find(
    (asset) =>
      matchers[platform].test(asset.name) &&
      !/-server-/i.test(asset.name),
  );
}

export function getWindowsClientAsset(release: GitHubRelease) {
  return getClientAsset(release, "windows");
}

export function formatBytes(bytes: number) {
  const mb = bytes / 1024 / 1024;
  return `${mb.toFixed(1)} MB`;
}
