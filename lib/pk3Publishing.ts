"use client";

import { BlobReader, ZipReader } from "@zip.js/zip.js";
import { sha256 } from "@noble/hashes/sha2.js";
import type { SupabaseClient } from "@supabase/supabase-js";
import { isMapHash, isMapId } from "@/lib/communityMaps";
import { inspectMapPackage, mintCommunityTicket, type InspectedMap,
  type TransferProgress } from "@/lib/mapPublishing";

import {
  MAX_PK3_SOURCE_BYTES, MAX_CONVERTED_PACKAGE_BYTES,
  PK3_UPLOAD_CHUNK_BYTES as UPLOAD_CHUNK_BYTES,
  pk3ConversionUrl, validPk3RoomName, validConversionJobId, validPk3Level,
} from "@/lib/pk3Contract";
export { MAX_PK3_SOURCE_BYTES, MAX_CONVERTED_PACKAGE_BYTES,
  pk3ConversionUrl, validPk3RoomName } from "@/lib/pk3Contract";
const HASH_BLOCK_BYTES = 4 * 1024 * 1024;
const WORKER_POLL_MS = 2500;

export type ConvertedPk3Job = {
  jobId: string;
  status: "queued" | "running" | "ready" | "failed";
  stage: string;
  hash: string | null;
  bytes: number | null;
  mapId: string | null;
  roomName: string;
  levelName: string | null;
  warnings: string[];
  reason: string | null;
};
export type Pk3Progress = TransferProgress;

export async function scanPk3Levels(file: File): Promise<string[]> {
  if (!file.name.toLowerCase().endsWith(".pk3") ||
      file.size === 0 || file.size > MAX_PK3_SOURCE_BYTES) {
    throw new Error("Choose a Quake .pk3 up to 128 MiB.");
  }
  const zip = new ZipReader(new BlobReader(file));
  try {
    const entries = await zip.getEntries();
    if (entries.length > 20000) throw new Error("Quake archive has too many ZIP entries.");
    const levels = new Set<string>();
    for (const entry of entries) {
      const canonical = entry.filename.replaceAll("\\", "/");
      // Normal ZIP archives often include explicit directory entries that
      // end in '/'. Match the stricter server-side validator's TrimEnd rule.
      const segments = canonical.replace(/\/$/, "").split("/");
      if (entry.filename !== canonical || canonical.startsWith("/") ||
          canonical.includes(":") ||
          segments.some(segment => segment === "" ||
            segment === "." || segment === "..") ||
          entry.uncompressedSize > 128 * 1024 * 1024) {
        throw new Error("Quake source contains an unsupported or unsafe archive path.");
      }
      const match = /^maps\/([A-Za-z0-9_.-]{1,80})\.bsp$/i.exec(canonical);
      if (match && !entry.directory) levels.add(match[1]);
    }
    if (levels.size === 0) throw new Error("No playable maps/*.bsp level found inside the .pk3.");
    if (levels.size > 64) throw new Error("Too many BSP levels in one web conversion.");
    return [...levels].sort((a,b) => a.localeCompare(b));
  } finally { await zip.close(); }
}

async function sourceHash(file: File, signal: AbortSignal,
  progress: (p: Pk3Progress) => void): Promise<string> {
  const hasher = sha256.create();
  for (let offset = 0; offset < file.size; offset += HASH_BLOCK_BYTES) {
    if (signal.aborted) throw new DOMException("PK3 upload paused.", "AbortError");
    const buffer = await file.slice(offset,
      Math.min(offset + HASH_BLOCK_BYTES, file.size)).arrayBuffer();
    hasher.update(new Uint8Array(buffer));
    progress({ phase: "hashing", done: Math.min(file.size, offset + HASH_BLOCK_BYTES),
      total: file.size, message: "Verifying source PK3 identity…" });
  }
  return Array.from(hasher.digest(), value => value.toString(16).padStart(2, "0")).join("");
}

function serviceUrl(path: string): string {
  const base = pk3ConversionUrl();
  if (!base) throw new Error("Website PK3 conversion service has not been configured.");
  return new URL(path, base).toString();
}

type SourceState = {
  hash: string;
  bytes: number;
  offset: number;
  chunkBytes: number;
  complete: boolean;
  jobId: string | null;
};
type WorkerError = { error?: string; offset?: number };

export function isSourceState(v: unknown, hash: string, bytes: number): v is SourceState {
  if (!v || typeof v !== "object") return false;
  const x = v as Partial<SourceState>;
  return x.hash === hash && x.bytes === bytes &&
    Number.isInteger(x.offset) && x.offset! >= 0 && x.offset! <= bytes &&
    Number.isInteger(x.chunkBytes) && x.chunkBytes! > 0 &&
    x.chunkBytes! <= UPLOAD_CHUNK_BYTES;
}
export function isConvertedJob(v: unknown): v is ConvertedPk3Job {
  if (!v || typeof v !== "object") return false;
  const x = v as Partial<ConvertedPk3Job>;
  return typeof x.jobId === "string" && validConversionJobId(x.jobId) &&
    ["queued", "running", "ready", "failed"].includes(x.status || "") &&
    typeof x.roomName === "string" && Array.isArray(x.warnings);
}
const wait = (ms: number, signal: AbortSignal) => new Promise<void>((resolve, reject) => {
  if (signal.aborted) { reject(new DOMException("Paused", "AbortError")); return; }
  const abort = () => { clearTimeout(timer); reject(new DOMException("Paused", "AbortError")); };
  const timer = setTimeout(() => { signal.removeEventListener("abort", abort); resolve(); }, ms);
  signal.addEventListener("abort", abort, { once: true });
});

function requester(client: SupabaseClient, signal: AbortSignal) {
  let ticket = "";
  async function call(method: string, route: string, body?: Blob | string): Promise<Response> {
    if (!ticket) ticket = await mintCommunityTicket(client);
    for (let turn = 0; turn < 2; turn++) {
      const response = await fetch(serviceUrl(route), {
        method, cache: "no-store", redirect: "error", signal,
        headers: {
          Authorization: "Bearer " + ticket,
          ...(typeof body === "string" ? { "Content-Type": "application/json" } :
            body instanceof Blob ? { "Content-Type": "application/octet-stream" } : {}),
        },
        body,
      });
      if (response.status !== 401 || turn === 1) return response;
      ticket = await mintCommunityTicket(client);
    }
    throw new Error("Hunter License ticket expired.");
  }
  return call;
}

async function requireResponse(response: Response, phase: string) {
  if (response.ok) return;
  const payload = await response.json().catch(() => ({})) as WorkerError;
  throw new Error(
    response.status === 401 ? "Hunter License sign-in expired." :
    response.status === 403 ? "This source does not belong to your Hunter License." :
    response.status === 507 ? "Conversion quota reached. Try again after older jobs expire." :
    response.status === 413 ? "The map source exceeds the server upload limit." :
    response.status === 422 ? "Source SHA-256 failed. Re-select the original PK3." :
    response.status === 409 ? phase + " conflict: " + (payload.error || "review the active job") :
    response.status >= 500 ? "PK3 service unavailable. Your uploaded bytes are retained." :
    phase + " failed: HTTP " + response.status,
  );
}

export async function submitPk3Conversion(args: {
  file: File;
  roomName: string;
  levelName: string;
  rightsConfirmed: boolean;
  client: SupabaseClient;
  signal: AbortSignal;
  progress: (p: Pk3Progress) => void;
}): Promise<ConvertedPk3Job> {
  const { file, roomName, levelName, rightsConfirmed, client, signal, progress } = args;
  if (!pk3ConversionUrl()) throw new Error("PK3 converter is not deployed yet.");
  if (!validPk3RoomName(roomName) || !validPk3Level(levelName))
    throw new Error("Choose a supported map name and BSP level.");
  if (!rightsConfirmed) throw new Error("Confirm that you have rights to redistribute all source assets.");
  if (file.size <= 0 || file.size > MAX_PK3_SOURCE_BYTES)
    throw new Error("Website PK3 source must be 128 MiB or smaller.");

  const hash = await sourceHash(file, signal, progress);
  const request = requester(client, signal);
  const route = "v1/sources/" + hash;
  progress({ phase: "starting", done: 0, total: file.size,
    message: "Reserving private source upload…" });
  const begin = await request("POST", route, JSON.stringify({
    bytes: file.size, roomName, levelName, rightsConfirmed: true,
  }));
  await requireResponse(begin, "Source reservation");
  const initial: unknown = await begin.json();
  if (!isSourceState(initial, hash, file.size))
    throw new Error("Converter returned an invalid source upload state.");
  let state = initial;
  let failures = 0;
  while (!state.complete && state.offset < file.size) {
    if (signal.aborted) throw new DOMException("Upload paused.", "AbortError");
    const chunk = file.slice(state.offset,
      Math.min(file.size, state.offset + Math.min(state.chunkBytes, UPLOAD_CHUNK_BYTES)));
    const response = await request("PUT", route + "?offset=" + state.offset, chunk);
    if (response.status === 409) {
      const current = await request("GET", route);
      await requireResponse(current, "Upload offset recovery");
      const recovery: unknown = await current.json();
      if (!isSourceState(recovery, hash, file.size))
        throw new Error("Converter returned an invalid recovery offset.");
      if (recovery.offset === state.offset) await requireResponse(response, "Upload offset");
      state = recovery;
      continue;
    }
    if ([429, 503].includes(response.status) && failures++ < 6) {
      await wait(Math.min(6000, 400 * failures * failures), signal);
      continue;
    }
    await requireResponse(response, "PK3 source upload");
    const next: unknown = await response.json();
    if (!isSourceState(next, hash, file.size) ||
        (next.offset <= state.offset && !next.complete)) {
      throw new Error("Converter source offset did not advance.");
    }
    state = next;
    failures = 0;
    progress({ phase: "uploading", done: state.offset, total: file.size,
      message: "Uploaded " + Math.round(state.offset * 100 / file.size) + "% of PK3…" });
  }
  let jobId = state.jobId;
  if (!state.complete) {
    const completion = await request("POST", route + "/complete");
    await requireResponse(completion, "PK3 SHA-256 verification");
    const body = await completion.json() as { jobId?: string };
    jobId = body.jobId || null;
  }
  if (!jobId || !validConversionJobId(jobId)) throw new Error("Conversion job identity is invalid.");

  for (;;) {
    if (signal.aborted) throw new DOMException("Conversion polling paused.", "AbortError");
    const reply = await request("GET", "v1/conversions/" + jobId);
    await requireResponse(reply, "Conversion status");
    const status: unknown = await reply.json();
    if (!isConvertedJob(status) || status.jobId !== jobId)
      throw new Error("Invalid converter job status.");
    if (status.status === "failed")
      throw new Error(status.reason || "Quake conversion failed. Try importing this map in Studio.");
    if (status.status === "ready") {
      if (!isMapHash(status.hash || "") || !isMapId(status.mapId || "") ||
          typeof status.bytes !== "number" ||
          status.bytes <= 0 || status.bytes > MAX_CONVERTED_PACKAGE_BYTES) {
        throw new Error("Conversion result failed identity/size validation.");
      }
      progress({ phase: "ready", done: file.size, total: file.size,
        message: "Converted package ready for creator review." });
      return status;
    }
    progress({ phase: "converting", done: file.size, total: file.size,
      message: status.stage || "Converting Quake BSP…" });
    await wait(WORKER_POLL_MS, signal);
  }
}

export async function retrieveConvertedMap(args: {
  job: ConvertedPk3Job;
  client: SupabaseClient;
  signal: AbortSignal;
  progress: (p: Pk3Progress) => void;
}): Promise<InspectedMap> {
  const { job, client, signal, progress } = args;
  if (!validConversionJobId(job.jobId) || job.status !== "ready" ||
      !isMapHash(job.hash || "") || !isMapId(job.mapId || "")) {
    throw new Error("Invalid conversion result.");
  }
  const request = requester(client, signal);
  progress({ phase: "retrieving", done: 0, total: job.bytes || 1,
    message: "Retrieving verified portable map package…" });
  const response = await request("GET", "v1/conversions/" + job.jobId + "/package");
  await requireResponse(response, "Portable package download");
  const announcedBytes = Number(response.headers.get("content-length"));
  const announcedHash = response.headers.get("x-prime-sha256");
  if (announcedHash !== job.hash || !Number.isSafeInteger(announcedBytes) ||
      announcedBytes !== job.bytes || announcedBytes > MAX_CONVERTED_PACKAGE_BYTES) {
    throw new Error("Converted package digest or size differs from verified worker result.");
  }
  // The platform may spill large blobs to temporary storage. This is the
  // deliberate compatibility handoff to the existing .ppmap publisher.
  const blob = await response.blob();
  if (signal.aborted) throw new DOMException("Download paused.", "AbortError");
  if (blob.size !== job.bytes) throw new Error("Incomplete converted map package.");
  const file = new File([blob], job.hash + ".ppmap", {
    type: "application/octet-stream",
  });
  const inspected = await inspectMapPackage(file, signal, progress);
  if (inspected.hash !== job.hash ||
      inspected.manifest.mapId.toLowerCase() !== job.mapId!.toLowerCase()) {
    throw new Error("Converted map failed end-to-end SHA-256 or MapId verification.");
  }
  return inspected;
}
