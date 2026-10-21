// The main creator picker routes each archive by extension. Preflight is
// only a convenience: the Community service and converter validate the bytes.
export type MapUploadKind = "ppmap" | "pk3";

export function getMapUploadKind(filename: string): MapUploadKind | null {
  const lower = filename.toLowerCase();
  if (lower.endsWith(".ppmap")) return "ppmap";
  if (lower.endsWith(".pk3")) return "pk3";
  return null;
}

export const MAX_BULK_MAPS = 30;
export const MAX_PPMAP_BYTES = 512 * 1024 * 1024;
export const MAX_BATCH_PK3_BYTES = 128 * 1024 * 1024;

export type MapFileDescription = {
  name: string;
  size: number;
  lastModified: number;
};

export function mapFileSelectionKey(file: MapFileDescription): string {
  return JSON.stringify([file.name, file.size, file.lastModified]);
}

export function planMapBatch<T extends MapFileDescription>(
  files: readonly T[],
  existing: readonly MapFileDescription[],
): {
  accepted: { file: T; kind: MapUploadKind; key: string }[];
  rejected: { file: T; reason: string }[];
} {
  const keys = new Set(existing.map(mapFileSelectionKey));
  const accepted: { file: T; kind: MapUploadKind; key: string }[] = [];
  const rejected: { file: T; reason: string }[] = [];
  for (const file of files) {
    const kind = getMapUploadKind(file.name);
    const key = mapFileSelectionKey(file);
    let reason: string | null = null;
    if (!kind) reason = "Only .ppmap and .pk3 files are supported.";
    else if (!Number.isSafeInteger(file.size) || file.size <= 0)
      reason = "The archive is empty or has an invalid size.";
    else if (file.size > (kind === "pk3" ? MAX_BATCH_PK3_BYTES : MAX_PPMAP_BYTES))
      reason = kind === "pk3" ? "PK3 exceeds 128 MiB." : "PPMAP exceeds 512 MiB.";
    else if (keys.has(key)) reason = "This file is already in the queue.";
    else if (existing.length + accepted.length >= MAX_BULK_MAPS)
      reason = "The batch is full (maximum 30 maps).";
    if (reason) rejected.push({ file, reason });
    else {
      accepted.push({ file, kind: kind!, key });
      keys.add(key);
    }
  }
  return { accepted, rejected };
}
