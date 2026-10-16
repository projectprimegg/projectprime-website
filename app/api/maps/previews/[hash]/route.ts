import { communityApiUrl, isMapHash } from "@/lib/communityMaps";

export const runtime = "nodejs";

export async function GET(
  _request: Request,
  context: { params: Promise<{ hash: string }> },
) {
  const { hash } = await context.params;
  if (!isMapHash(hash)) return new Response(null, { status: 404 });
  try {
    const upstream = await fetch(communityApiUrl("packages/" + hash + "/preview"), {
      redirect: "error",
      signal: AbortSignal.timeout(8000),
      next: { revalidate: 300 },
    });
    if (!upstream.ok) return new Response(null, { status: 404 });
    const declared = Number(upstream.headers.get("content-length"));
    if (declared > 1024 * 1024) return new Response(null, { status: 413 });
    const bytes = await upstream.arrayBuffer();
    if (bytes.byteLength > 1024 * 1024) return new Response(null, { status: 413 });
    const magic = new Uint8Array(bytes).slice(0, 8);
    if (magic.length < 8 ||
        ![137, 80, 78, 71, 13, 10, 26, 10].every((value, i) => magic[i] === value)) {
      return new Response(null, { status: 415 });
    }
    return new Response(bytes, {
      headers: {
        "Content-Type": "image/png",
        "Cache-Control": "public, max-age=300, s-maxage=300",
        "X-Content-Type-Options": "nosniff",
      },
    });
  } catch {
    return new Response(null, { status: 502 });
  }
}
