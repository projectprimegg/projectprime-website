// Public Community-map API contract. Keep this in sync with the game's v2
// map project DTOs without introducing a separate website map database.
export type MapPackage = {
  hash: string;
  mapId: string;
  name: string;
  displayName: string | null;
  author: string | null;
  version: string | null;
  bytes: number;
  supportedModes: string[];
  minPlayers: number;
  maxPlayers: number;
  minimumProtocol: number;
  favoriteCount: number;
  publishedAt: string;
  listed: boolean;
  draft: boolean;
};

export type MapRevision = {
  revisionNumber: number;
  hash: string;
  parentHash: string | null;
  createdAt: string;
  releaseNotes: string | null;
  package: MapPackage;
};

export type MapProject = {
  mapId: string;
  ownerId: string;
  name: string;
  displayName: string | null;
  author: string | null;
  currentHash: string | null;
  latestHash: string;
  revisionCount: number;
  createdAt: string;
  updatedAt: string;
  currentRevision: MapRevision | null;
  latestRevision: MapRevision;
  archivedAt?: string | null;
  deletedAt?: string | null;
  canManageLifecycle?: boolean;
};

export type MapBrowse = { items: MapProject[]; total: number };
export type MapFilters = { query?: string; sort?: string; page?: number; mode?: string };

const HASH = /^[0-9a-f]{64}$/;
const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export function isMapHash(value: string): boolean { return HASH.test(value); }
export function isMapId(value: string): boolean { return GUID.test(value); }

// One HTTPS service for both the browser uploader and server-rendered catalog.
// Operator must configure NEXT_PUBLIC_MAP_SERVICE_URL when the origin moves.
export function communityServiceUrl(): URL {
  const configured = process.env.NEXT_PUBLIC_MAP_SERVICE_URL || "https://maps.rebooty.xyz/";
  const address = new URL(configured.endsWith("/") ? configured : configured + "/");
  if (address.protocol !== "https:" &&
      !(address.protocol === "http:" && ["localhost", "127.0.0.1"].includes(address.hostname))) {
    throw new Error("The Community service URL must use HTTPS.");
  }
  if (address.username || address.password || address.search || address.hash) {
    throw new Error("The Community service URL cannot contain credentials or query parameters.");
  }
  return address;
}

export function communityApiUrl(path: string): string {
  return new URL(path, communityServiceUrl()).toString();
}

function isProject(value: unknown): value is MapProject {
  if (!value || typeof value !== "object") return false;
  const project = value as Partial<MapProject>;
  const revision = project.currentRevision ?? project.latestRevision;
  return typeof project.mapId === "string" && isMapId(project.mapId)
    && typeof project.name === "string"
    && !!revision && !!revision.package
    && typeof revision.package.hash === "string" && isMapHash(revision.package.hash);
}

async function getJson(path: string): Promise<Response> {
  const response = await fetch(communityApiUrl(path), {
    headers: { Accept: "application/json" },
    redirect: "error",
    next: { revalidate: 60 },
    signal: AbortSignal.timeout(8000),
  });
  if (!response.ok) throw new Error("Community map service returned " + response.status);
  return response;
}

export async function browseCommunityMaps(filters: MapFilters = {}): Promise<MapBrowse> {
  const query = new URLSearchParams();
  query.set("page", String(Math.max(1, Math.min(2000, filters.page || 1))));
  query.set("pageSize", "24");
  query.set("sort", ["name", "new", "updated", "favorites"].includes(filters.sort || "")
    ? filters.sort! : "updated");
  if (filters.query) query.set("query", filters.query.slice(0, 120));
  if (filters.mode) query.set("mode", filters.mode.slice(0, 40));
  const response = await getJson("v2/maps?" + query.toString());
  const data: unknown = await response.json();
  if (!Array.isArray(data)) throw new Error("Unexpected Community catalog response.");
  const items = data.filter(isProject);
  const count = Number(response.headers.get("X-Total-Count"));
  return { items, total: Number.isFinite(count) && count >= items.length ? count : items.length };
}

export async function getCommunityMap(mapId: string): Promise<MapProject | null> {
  if (!isMapId(mapId)) return null;
  const response = await getJson("v2/maps/" + encodeURIComponent(mapId));
  const data: unknown = await response.json();
  return isProject(data) ? data : null;
}

export async function getCommunityRevisions(mapId: string): Promise<MapRevision[]> {
  if (!isMapId(mapId)) return [];
  const response = await getJson("v2/maps/" + encodeURIComponent(mapId) + "/revisions");
  const data: unknown = await response.json();
  if (!Array.isArray(data)) return [];
  return data.filter((row): row is MapRevision =>
    !!row && typeof row === "object" &&
    typeof row.revisionNumber === "number" &&
    typeof row.hash === "string" && isMapHash(row.hash));
}

export function preferredPackage(project: MapProject): MapPackage {
  return (project.currentRevision ?? project.latestRevision).package;
}

export function mapTitle(project: MapProject): string {
  return project.displayName || project.name;
}

export function bytesLabel(bytes: number): string {
  return Number.isFinite(bytes) && bytes >= 0
    ? (bytes / (1024 * 1024)).toFixed(1) + " MiB"
    : "Unknown size";
}
