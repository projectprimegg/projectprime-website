// Pure website conversion validation, shared by runtime and CI contract tests.
export const MAX_PK3_SOURCE_BYTES = 128 * 1024 * 1024;
export const MAX_CONVERTED_PACKAGE_BYTES = 512 * 1024 * 1024;
export const PK3_UPLOAD_CHUNK_BYTES = 8 * 1024 * 1024;

const RESERVED = /^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$/i;
const ROOM = /^[A-Za-z0-9][A-Za-z0-9 _-]{0,39}$/;

export function validPk3RoomName(value: string): boolean {
  return value.length > 0 && value.length <= 40 &&
    ROOM.test(value) && value.trim() === value && !RESERVED.test(value);
}

// The converter is deployed under /pk3/ on the *existing* Community
// service's HTTPS origin. Operators may still set an explicit conversion URL.
// Never assume a live converter from configuration alone: the UI probes
// /health before enabling any PK3 source upload.
export function pk3ConversionUrl(): URL {
  const explicit = process.env.NEXT_PUBLIC_PK3_CONVERSION_URL?.trim();
  const mapService = process.env.NEXT_PUBLIC_MAP_SERVICE_URL?.trim() ||
    "https://maps.rebooty.xyz/";
  const source = explicit || new URL(
    "pk3/", mapService.endsWith("/") ? mapService : mapService + "/",
  ).toString();
  const url = new URL(source.endsWith("/") ? source : source + "/");
  if (url.protocol !== "https:" &&
      !(url.protocol === "http:" &&
        ["localhost", "127.0.0.1"].includes(url.hostname))) {
    throw new Error("PK3 conversion requires an HTTPS service.");
  }
  if (url.username || url.password || url.search || url.hash) {
    throw new Error("PK3 service address must not have credentials or query parameters.");
  }
  return url;
}

// Health is public but CORS-origin restricted by the conversion sidecar.
// Explicitly require its { ok: true } response, not just any HTTP 200
// (which could be the old Community map service or a proxy error page).
export async function isPk3ConverterReady(
  fetcher: typeof fetch = fetch,
  signal?: AbortSignal,
): Promise<boolean> {
  try {
    const reply = await fetcher(new URL("health", pk3ConversionUrl()), {
      method: "GET", cache: "no-store", redirect: "error", signal,
    });
    if (!reply.ok) return false;
    const data: unknown = await reply.json();
    return !!data && typeof data === "object" && "ok" in data && data.ok === true;
  } catch {
    return false;
  }
}

export function validConversionJobId(value: unknown): value is string {
  return typeof value === "string" && /^[a-f0-9-]{36}$/.test(value);
}

export function validPk3Level(value: unknown): value is string {
  return typeof value === "string" &&
    /^[A-Za-z0-9_.-]{1,80}$/.test(value) &&
    value !== "." && value !== ".." && !value.startsWith(".");
}
