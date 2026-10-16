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

export function pk3ConversionUrl(): URL | null {
  const source = process.env.NEXT_PUBLIC_PK3_CONVERSION_URL?.trim();
  if (!source) return null;
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

export function validConversionJobId(value: unknown): value is string {
  return typeof value === "string" && /^[a-f0-9-]{36}$/.test(value);
}

export function validPk3Level(value: unknown): value is string {
  return typeof value === "string" &&
    /^[A-Za-z0-9_.-]{1,80}$/.test(value) &&
    value !== "." && value !== ".." && !value.startsWith(".");
}
