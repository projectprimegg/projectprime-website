import { NextResponse } from "next/server";

export const runtime = "nodejs";

function result(status: number, data: unknown) {
  return NextResponse.json(data, {
    status,
    headers: { "Cache-Control": "no-store", "Pragma": "no-cache" },
  });
}

// Browser JWTs are sent to this same-origin endpoint only to mint a scoped,
// short-lived Community ticket. The website never stores credentials, and
// the original Supabase access token is never sent to the map service.
export async function POST(request: Request) {
  const origin = request.headers.get("origin");
  if (origin && origin !== new URL(request.url).origin) {
    return result(403, { error: "Cross-origin ticket requests are not allowed." });
  }
  if (request.headers.get("sec-fetch-site") === "cross-site") {
    return result(403, { error: "Cross-site ticket requests are not allowed." });
  }

  const authorization = request.headers.get("authorization");
  if (!authorization || !/^Bearer [^\s]{20,4096}$/.test(authorization)) {
    return result(401, { error: "Hunter License sign-in required." });
  }

  const url = process.env.NEXT_PUBLIC_SUPABASE_URL ||
    "https://hwcjaygoistufktorbmf.supabase.co";
  const key = process.env.NEXT_PUBLIC_SUPABASE_PUBLISHABLE_KEY ||
    "sb_publishable_EVT45OPl638kA_j8vZ0ebg_sw3aWaVz";

  try {
    const upstream = await fetch(new URL("/functions/v1/community-map-ticket", url), {
      method: "POST",
      headers: {
        Authorization: authorization,
        apikey: key,
        "Content-Type": "application/json",
        Accept: "application/json",
      },
      body: JSON.stringify({ action: "mint" }),
      cache: "no-store",
      redirect: "error",
      signal: AbortSignal.timeout(12000),
    });
    if (!upstream.ok) {
      return result(upstream.status === 401 ? 401 : 502, {
        error: upstream.status === 401 ? "Hunter License session expired." :
          "Unable to issue a Community upload ticket.",
      });
    }
    const payload: unknown = await upstream.json();
    if (!payload || typeof payload !== "object" ||
        !("ticket" in payload) || typeof payload.ticket !== "string" ||
        !payload.ticket.startsWith("ppm1.") || payload.ticket.length > 3072) {
      return result(502, { error: "Community ticket response was invalid." });
    }
    return result(200, { ticket: payload.ticket });
  } catch {
    return result(502, { error: "Hunter License service is temporarily unavailable." });
  }
}
