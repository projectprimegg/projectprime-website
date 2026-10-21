import { BlobReader, TextWriter, ZipReader, type Entry } from "@zip.js/zip.js";
import { isMapHash, isMapId } from "./communityMaps";
import {
  getMapUploadKind, MAX_BATCH_PK3_BYTES, MAX_PPMAP_BYTES,
  type MapUploadKind,
} from "./mapUploadFormats";

const MAX_PACKAGE_ENTRIES = 2048;
const MAX_PK3_ENTRIES = 20000;
const MAX_ENTRY_NAME_LENGTH = 65535;
const MAX_ENTRY_COMMENT_LENGTH = 65535;
const MAX_ENTRY_EXTRA_FIELD_BYTES = 65535;
const MAX_MANIFEST_BYTES = 65536;
const MAX_PK3_LEVELS = 64;

export type PackageManifest = {
  format: number;
  mapId: string;
  name: string;
  displayName?: string | null;
  mapVersion?: string | null;
  contentHash: string;
};

export type MapArchiveInspection =
  | { kind: "ppmap"; manifest: PackageManifest }
  | { kind: "pk3"; levels: string[] };

function abortIfRequested(signal?: AbortSignal): void {
  if (signal?.aborted) throw new DOMException("Inspection paused.", "AbortError");
}

function assertSupportedFilename(file: File): MapUploadKind {
  const extensionKind = getMapUploadKind(file.name);
  if (!extensionKind) throw new Error("Only .ppmap and .pk3 files are supported.");
  return extensionKind;
}

function validateManifest(value: unknown): PackageManifest {
  if (!value || typeof value !== "object" || Array.isArray(value)) {
    throw new Error("Invalid Project Prime manifest.json.");
  }
  const manifest = value as PackageManifest;
  if (![2, 3].includes(manifest.format) || !isMapId(manifest.mapId) ||
      typeof manifest.name !== "string" || !manifest.name ||
      !isMapHash(manifest.contentHash)) {
    throw new Error("Unsupported map format or invalid map identity.");
  }
  return manifest;
}

function isSafePk3Path(filename: string): boolean {
  const canonical = filename.replaceAll("\\", "/");
  const segments = canonical.replace(/\/$/, "").split("/");
  return filename === canonical && canonical.length > 0 &&
    !canonical.startsWith("/") && !canonical.includes(":") &&
    !segments.some(segment => segment === "" || segment === "." || segment === "..");
}

function filenameMismatch(actual: MapUploadKind): Error {
  if (actual === "ppmap") {
    return new Error("This archive contains a Project Prime map package but is named .pk3. Rename it to .ppmap and add it again.");
  }
  return new Error("This archive contains a Quake PK3 with maps/*.bsp but is named .ppmap. Rename it to .pk3 and add it again.");
}

/**
 * Inspect an accepted archive in one ZIP pass. The manifest takes precedence
 * over optional BSP files; the Community service remains authoritative for
 * publication validation.
 */
export async function inspectMapArchive(
  file: File,
  signal?: AbortSignal,
): Promise<MapArchiveInspection> {
  const extensionKind = assertSupportedFilename(file);
  if (!Number.isSafeInteger(file.size) || file.size <= 0 || file.size > MAX_PPMAP_BYTES) {
    throw new Error("Map archives must be between 1 byte and 512 MiB.");
  }
  if (extensionKind === "pk3" && file.size > MAX_BATCH_PK3_BYTES) {
    throw new Error("PK3 exceeds the 128 MiB source limit.");
  }

  abortIfRequested(signal);
  const archive = new ZipReader(new BlobReader(file), { checkAmbiguity: true });
  let inspection: MapArchiveInspection;
  let manifestEntry: Entry | null = null;
  let manifestCount = 0;
  let entryCount = 0;
  let invalidPk3Reason = "Quake source contains an unsupported or unsafe archive entry.";
  let hasInvalidPk3Entry = false;
  let hasSymlink = false;
  const levels = new Set<string>();

  try {
    abortIfRequested(signal);
    const entries = archive.getEntriesGenerator({
      checkAmbiguity: true,
      onprogress: () => abortIfRequested(signal),
    });
    for await (const entry of entries) {
      abortIfRequested(signal);
      entryCount++;
      if (entryCount > MAX_PK3_ENTRIES) {
        throw new Error("Map archive has too many ZIP entries.");
      }
      if (!Number.isSafeInteger(entry.compressedSize) || entry.compressedSize < 0 ||
          !Number.isSafeInteger(entry.uncompressedSize) || entry.uncompressedSize < 0 ||
          entry.filename.length > MAX_ENTRY_NAME_LENGTH ||
          entry.rawFilename.byteLength > MAX_ENTRY_NAME_LENGTH ||
          (entry.comment?.length || 0) > MAX_ENTRY_COMMENT_LENGTH ||
          entry.rawComment.byteLength > MAX_ENTRY_COMMENT_LENGTH ||
          entry.rawExtraField.byteLength > MAX_ENTRY_EXTRA_FIELD_BYTES) {
        throw new Error("Map archive contains invalid or oversized ZIP metadata.");
      }

      if (entry.filename === "manifest.json") {
        manifestCount++;
        if (manifestCount > 1) throw new Error("Archive contains duplicate root manifest.json files.");
        manifestEntry = entry;
      }

      const canonical = entry.filename.replaceAll("\\", "/");
      if (entry.symlink) hasSymlink = true;
      if (!isSafePk3Path(entry.filename) || entry.symlink ||
          entry.uncompressedSize > MAX_BATCH_PK3_BYTES) {
        hasInvalidPk3Entry = true;
      }
      const match = /^maps\/([A-Za-z0-9_.-]{1,80})\.bsp$/i.exec(canonical);
      if (match && !entry.directory && !entry.symlink) {
        levels.add(match[1]);
        if (levels.size > MAX_PK3_LEVELS) {
          hasInvalidPk3Entry = true;
          invalidPk3Reason = "Too many BSP levels in one web conversion.";
        }
      }
    }
    abortIfRequested(signal);

    if (manifestEntry) {
      if (entryCount > MAX_PACKAGE_ENTRIES) throw new Error("Too many package entries.");
      if (hasSymlink) throw new Error("Project Prime map packages cannot contain symbolic links.");
      if (manifestEntry.directory || manifestEntry.symlink || manifestEntry.encrypted ||
          manifestEntry.uncompressedSize > MAX_MANIFEST_BYTES) {
        throw new Error("Missing or invalid Project Prime manifest.json.");
      }
      abortIfRequested(signal);
      const raw = await manifestEntry.getData(new TextWriter(), {
        onstart: () => abortIfRequested(signal),
        onprogress: () => abortIfRequested(signal),
        onend: () => abortIfRequested(signal),
      });
      abortIfRequested(signal);
      if (typeof raw !== "string" || raw.length > MAX_MANIFEST_BYTES) {
        throw new Error("Map manifest exceeds the supported size.");
      }
      let data: unknown;
      try {
        data = JSON.parse(raw);
      } catch {
        throw new Error("Project Prime manifest.json is not valid JSON.");
      }
      inspection = { kind: "ppmap", manifest: validateManifest(data) };
    } else {
      if (hasInvalidPk3Entry) throw new Error(invalidPk3Reason);
      if (levels.size === 0) {
        throw new Error("No Project Prime manifest.json or playable maps/*.bsp level was found.");
      }
      inspection = { kind: "pk3", levels: [...levels].sort((a, b) => a.localeCompare(b)) };
    }
  } finally {
    await archive.close();
  }

  if (inspection.kind !== extensionKind) {
    throw filenameMismatch(inspection.kind);
  }
  return inspection;
}
