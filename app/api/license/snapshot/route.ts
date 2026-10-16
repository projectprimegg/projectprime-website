import { NextResponse } from "next/server";
import { isLicenseSnapshot } from "@/lib/hunterLicense";

export const runtime = "nodejs";
export const dynamic = "force-dynamic";

function json(status: number, payload: unknown) {
  return NextResponse.json(payload, {
    status,
    headers: { "Cache-Control": "private, no-store", "Pragma": "no-cache" },
  });
}

export async function POST(req: Request) {
  // A same-origin, one-user relay. Auth still occurs inside the authoritative
  // Hunter License Edge Function, which validates this Supabase JWT.
  const origin = req.headers.get("origin");
  if (origin && origin !== new URL(req.url).origin)
    return json(403, { error: "Cross-origin requests are not permitted." });
  if (req.headers.get("sec-fetch-site") === "cross-site")
    return json(403, { error: "Cross-site requests are not permitted." });

  const authorization = req.headers.get("authorization");
  if (!authorization || !/^Bearer [A-Za-z0-9._-]{20,4096}$/.test(authorization))
    return json(401, { error: "Hunter License sign-in required." });

  const url = process.env.NEXT_PUBLIC_SUPABASE_URL ||
    "https://hwcjaygoistufktorbmf.supabase.co";
  const key = process.env.NEXT_PUBLIC_SUPABASE_PUBLISHABLE_KEY ||
    "sb_publishable_EVT45OPl638kA_j8vZ0ebg_sw3aWaVz";

  try {
    const response = await fetch(new URL("/functions/v1/hunter-license", url), {
      method: "POST",
      headers: {
        Authorization: authorization,
        apikey: key,
        "Content-Type": "application/json",
        Accept: "application/json",
      },
      // The function seeds a missing license but never overwrites the saved
      // profile. Existing players retain their game nickname and career.
      body: JSON.stringify({ display_name: "Hunter", favorite_hunter: 0 }),
      cache: "no-store",
      redirect: "error",
      signal: AbortSignal.timeout(12000),
    });
    if (!response.ok) return json(
      response.status === 401 ? 401 : 502,
      { error: response.status === 401 ? "Your Hunter License session expired." :
        "Hunter License service is temporarily unavailable." },
    );

    if (Number(response.headers.get("content-length") || 0) > 256 * 1024)
      return json(502, { error: "Unexpected Hunter License response size." });
    const body: unknown = await response.json();
    if (!isLicenseSnapshot(body))
      return json(502, { error: "The Hunter License response was invalid." });
    return json(200, body);
  } catch {
    return json(502, { error: "Could not contact Hunter License service." });
  }
}
