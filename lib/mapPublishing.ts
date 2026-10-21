"use client";

import { BlobReader, TextWriter, ZipReader } from "@zip.js/zip.js";
import { sha256 } from "@noble/hashes/sha2.js";
import type { SupabaseClient } from "@supabase/supabase-js";
import {
  communityApiUrl, isMapHash, isMapId,
  type MapPackage, type MapProject,
} from "./communityMaps";
import { MapPublishSession } from "./mapPublishSession";
import { mapUploadRetryDelay, waitForMapUploadRetry } from "./mapUploadRetry";

const MAX_PACKAGE_BYTES = 512 * 1024 * 1024;
const HASH_BLOCK = 4 * 1024 * 1024;
const MAX_CHUNK = 8 * 1024 * 1024;
const MIN_CHUNK = 1 * 1024 * 1024;

export type Visibility = "Published" | "Unlisted" | "Draft";
export type PackageManifest = {
  format: number;
  mapId: string;
  name: string;
  displayName?: string | null;
  mapVersion?: string | null;
  contentHash: string;
};
export type InspectedMap = { file: File; manifest: PackageManifest; hash: string };
export type TransferProgress = {
  phase: string;
  done: number;
  total: number;
  message: string;
};
type UploadState = {
  packageHash: string;
  bytes: number;
  offset: number;
  chunkBytes: number;
  complete: boolean;
};
type Conflict = {
  code?: string;
  message?: string;
  latestHash?: string | null;
  resumeAvailable?: boolean;
};

export class MapPublishError extends Error {
  constructor(public readonly status: number, message: string,
              public readonly conflict?: Conflict) {
    super(message);
    this.name = "MapPublishError";
  }
}

export async function inspectMapPackage(
  file: File,
  signal: AbortSignal,
  progress: (value: TransferProgress) => void,
): Promise<InspectedMap> {
  if (!file.name.toLowerCase().endsWith(".ppmap")) {
    throw new Error("Choose a Project Prime .ppmap archive exported from Map Studio.");
  }
  if (!file.size || file.size > MAX_PACKAGE_BYTES) {
    throw new Error("Project Prime maps must be between 1 byte and 512 MiB.");
  }

  progress({ phase: "inspecting", done: 0, total: file.size,
    message: "Inspecting the map manifest…" });
  const archive = new ZipReader(new BlobReader(file));
  let manifest: PackageManifest;
  try {
    const entries = await archive.getEntries();
    if (entries.length > 2048) throw new Error("Too many package entries.");
    const entry = entries.find(item => item.filename === "manifest.json");
    if (!entry || entry.directory || entry.encrypted || entry.uncompressedSize > 65536) {
      throw new Error("Missing or invalid Project Prime manifest.json.");
    }
    const raw = await entry.getData(new TextWriter());
    if (typeof raw !== "string" || raw.length > 65536) {
      throw new Error("Map manifest exceeds the supported size.");
    }
    const data: unknown = JSON.parse(raw);
    if (!data || typeof data !== "object") throw new Error("Invalid map manifest.");
    manifest = data as PackageManifest;
    if (![2, 3].includes(manifest.format) || !isMapId(manifest.mapId) ||
        !manifest.name || typeof manifest.name !== "string" ||
        !isMapHash(manifest.contentHash)) {
      throw new Error("Unsupported map format or invalid map identity.");
    }
  } finally {
    await archive.close();
  }

  // Hash incrementally. Never keep 512 MiB of file bytes in browser memory.
  const hasher = sha256.create();
  for (let offset = 0; offset < file.size; offset += HASH_BLOCK) {
    if (signal.aborted) throw new DOMException("Operation paused.", "AbortError");
    const block = await file.slice(offset, Math.min(offset + HASH_BLOCK, file.size)).arrayBuffer();
    if (signal.aborted) throw new DOMException("Operation paused.", "AbortError");
    hasher.update(new Uint8Array(block));
    progress({ phase: "hashing", done: Math.min(offset + HASH_BLOCK, file.size),
      total: file.size, message: "Calculating exact archive SHA-256…" });
  }
  const hash = Array.from(hasher.digest(), b => b.toString(16).padStart(2, "0")).join("");
  return { file, manifest, hash };
}

async function mintCommunityCredential(client: SupabaseClient): Promise<{
  ticket: string;
  ownerId: string;
}> {
  const { data, error } = await client.auth.getSession();
  if (error || !data.session?.access_token || !data.session.user?.id) {
    throw new Error("Your Hunter License session expired. Sign in again.");
  }
  const response = await fetch("/api/maps/ticket", {
    method: "POST",
    headers: { Authorization: "Bearer " + data.session.access_token },
    cache: "no-store",
  });
  const payload = await response.json().catch(() => ({})) as { ticket?: string; error?: string };
  if (!response.ok || !payload.ticket) {
    throw new Error(payload.error || "Could not obtain a Community upload ticket.");
  }
  return { ticket: payload.ticket, ownerId: data.session.user.id };
}

export async function mintCommunityTicket(client: SupabaseClient): Promise<string> {
  return (await mintCommunityCredential(client)).ticket;
}

async function api(
  ticket: string,
  method: string,
  path: string,
  body?: BodyInit,
  signal?: AbortSignal,
): Promise<Response> {
  const headers: Record<string, string> = { Authorization: "Bearer " + ticket };
  if (typeof body === "string") headers["Content-Type"] = "application/json";
  if (body instanceof Blob) headers["Content-Type"] = "application/octet-stream";
  return fetch(communityApiUrl(path), {
    method, headers, body, cache: "no-store", signal, redirect: "error",
  });
}

async function ensure(response: Response): Promise<void> {
  if (response.ok) return;
  const data: Conflict | null = await response.json().catch(() => null);
  const message = data?.message || (response.status === 409
    ? "The map changed on the server. Review the latest revision before publishing."
    : response.status === 401 ? "Creator ticket expired."
    : response.status === 403 ? "This Hunter License cannot publish this map."
    : response.status === 413 ? "The server rejected this upload size."
    : "Community service returned HTTP " + response.status);
  throw new MapPublishError(response.status, message, data || undefined);
}

function validState(value: UploadState, expected: InspectedMap): UploadState {
  if (value.packageHash !== expected.hash || value.bytes !== expected.file.size ||
      !Number.isInteger(value.offset) || value.offset < 0 ||
      value.offset > value.bytes || !Number.isInteger(value.chunkBytes) ||
      value.chunkBytes <= 0 || value.chunkBytes > 50 * 1024 * 1024) {
    throw new Error("The Community service returned an invalid upload offset.");
  }
  return value;
}

export async function ownedMapProjects(
  ticket: string, signal?: AbortSignal,
): Promise<MapProject[]> {
  const response = await api(ticket, "GET", "v2/maps?mine=true&pageSize=100", undefined, signal);
  await ensure(response);
  const data: unknown = await response.json();
  if (!Array.isArray(data)) throw new Error("Creator map catalog is invalid.");
  return data.filter((item): item is MapProject => !!item &&
    typeof item === "object" && typeof item.mapId === "string" &&
    typeof item.latestHash === "string");
}

export async function publishMapPackage(args: {
  map: InspectedMap;
  client: SupabaseClient;
  visibility: Visibility;
  notes: string;
  signal: AbortSignal;
  progress: (value: TransferProgress) => void;
  publishSession?: MapPublishSession;
}): Promise<MapPackage> {
  const { map, client, visibility, signal, progress } = args;
  if (args.notes.length > 4000) throw new Error("Release notes are limited to 4,000 characters.");
  const publishSession = args.publishSession ?? new MapPublishSession();
  const credential = await mintCommunityCredential(client);
  const currentOwnerId = credential.ownerId;
  let ticket = credential.ticket;
  const expectedParentHash = await publishSession.latestHashFor(
    currentOwnerId,
    map.manifest.mapId,
    async () => {
      const mine = await ownedMapProjects(ticket, signal);
      return new Map(mine.map(project =>
        [project.mapId.toLowerCase(), project.latestHash] as const));
    },
  );

  const start = {
    bytes: map.file.size,
    listed: visibility === "Published",
    draft: visibility === "Draft",
    mapId: map.manifest.mapId,
    existingMap: expectedParentHash !== null,
    expectedParentHash,
    releaseNotes: args.notes.trim() || null,
    allowStaleParent: false,
  };

  progress({ phase: "starting", done: 0, total: map.file.size,
    message: "Reserving upload and checking revision history…" });
  let response = await api(ticket, "POST", "uploads/" + map.hash,
    JSON.stringify(start), signal);
  await ensure(response);
  let state = validState(await response.json() as UploadState, map);
  let maxChunk = Math.min(state.chunkBytes, MAX_CHUNK);
  let retry = 0;
  let refreshed = 0;

  while (!state.complete && state.offset < map.file.size) {
    if (signal.aborted) throw new DOMException("Upload paused.", "AbortError");
    const nextEnd = Math.min(state.offset + maxChunk, map.file.size);
    const chunk = map.file.slice(state.offset, nextEnd);
    progress({ phase: "uploading", done: state.offset, total: map.file.size,
      message: "Uploading verified map package…" });
    response = await api(ticket, "PUT",
      "uploads/" + map.hash + "?offset=" + state.offset, chunk, signal);

    if (response.status === 413 && maxChunk > MIN_CHUNK) {
      maxChunk = Math.max(MIN_CHUNK, Math.floor(maxChunk / 2));
      continue;
    }
    if (response.status === 401 && refreshed++ < 2) {
      const renewed = await mintCommunityCredential(client);
      if (renewed.ownerId !== currentOwnerId) {
        throw new Error(
          "Your Hunter License changed during this upload. Restart the batch to use the new account.");
      }
      ticket = renewed.ticket;
      continue;
    }
    if ((response.status === 429 || response.status === 503) && retry++ < 8) {
      await waitForMapUploadRetry(mapUploadRetryDelay(response, retry), signal);
      continue;
    }
    if (response.status === 409) {
      const current = await api(ticket, "GET", "uploads/" + map.hash,
        undefined, signal);
      await ensure(current);
      const next = validState(await current.json() as UploadState, map);
      if (next.offset === state.offset) await ensure(response);
      state = next;
      continue;
    }
    await ensure(response);
    const next = validState(await response.json() as UploadState, map);
    if (!next.complete && next.offset <= state.offset) {
      throw new Error("The server did not advance the upload offset.");
    }
    state = next;
    retry = 0;
    progress({ phase: "uploading", done: state.offset, total: map.file.size,
      message: "Uploaded " + Math.round(state.offset * 100 / map.file.size) + "%…" });
  }

  progress({ phase: "publishing", done: map.file.size, total: map.file.size,
    message: "Validating the immutable package on the server…" });
  if (!state.complete) {
    response = await api(ticket, "POST", "uploads/" + map.hash + "/complete",
      undefined, signal);
    if (response.status === 401) {
      const renewed = await mintCommunityCredential(client);
      if (renewed.ownerId !== currentOwnerId) {
        throw new Error(
          "Your Hunter License changed during this upload. Restart the batch to use the new account.");
      }
      ticket = renewed.ticket;
      response = await api(ticket, "POST", "uploads/" + map.hash + "/complete",
        undefined, signal);
    }
    await ensure(response);
  } else {
    response = await api(ticket, "GET", "packages/" + map.hash + "/metadata",
      undefined, signal);
    await ensure(response);
  }
  const published = await response.json() as MapPackage;
  if (published.hash !== map.hash ||
      published.mapId.toLowerCase() !== map.manifest.mapId.toLowerCase()) {
    throw new Error("Server publication returned a different immutable map identity.");
  }
  publishSession.recordPublished(currentOwnerId, published.mapId, published.hash);
  return published;
}
