// The main creator upload picker routes by extension; each handler still
// validates the archive contents before conversion or publication.
export type MapUploadKind = "ppmap" | "pk3";

export function getMapUploadKind(filename: string): MapUploadKind | null {
  const lower = filename.toLowerCase();
  if (lower.endsWith(".ppmap")) return "ppmap";
  if (lower.endsWith(".pk3")) return "pk3";
  return null;
}
