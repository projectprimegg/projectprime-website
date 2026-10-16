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
    // Bound actual streamed bytes, not just an untrusted Content-Length.
    // This route must never buffer a malicious multi-gigabyte image response.
    if (!upstream.body) return new Response(null, { status: 502 });
    const reader = upstream.body.getReader();
    const chunks: Uint8Array[] = [];
    let total = 0;
    while (true) {
      const { done, value } = await reader.read();
      if (done) break;
      total += value.byteLength;
      if (total > 1024 * 1024) {
        await reader.cancel();
        return new Response(null, { status: 413 });
      }
      chunks.push(value);
    }
    const bytes = new ArrayBuffer(total);
    const output = new Uint8Array(bytes);
    let index = 0;
    for (const chunk of chunks) {
      output.set(chunk, index);
      index += chunk.byteLength;
    }
    const magic = output.slice(0, 8);
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
