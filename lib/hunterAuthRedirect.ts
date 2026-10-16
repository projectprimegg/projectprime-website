// Email and OAuth callbacks use a reachable HTTPS site, not a local/preview origin.
// Local redirects require an explicit NEXT_PUBLIC_HUNTER_AUTH_ORIGIN override.
export type HunterAuthDestination = "/license" | "/maps/account";
const PRODUCTION_ORIGIN = "https://projectprime.gg";

export function hunterAuthRedirectUrl(
  destination: HunterAuthDestination,
  origin = process.env.NEXT_PUBLIC_HUNTER_AUTH_ORIGIN || PRODUCTION_ORIGIN,
): string {
  if (destination !== "/license" && destination !== "/maps/account")
    throw new Error("Unsupported Hunter authentication destination.");
  const url = new URL(origin);
  const loopback = url.hostname === "localhost" || url.hostname === "127.0.0.1" ||
    url.hostname === "[::1]";
  if ((url.protocol !== "https:" && !(loopback && url.protocol === "http:")) ||
      url.username || url.password || url.pathname !== "/" || url.search || url.hash)
    throw new Error("Hunter Auth requires an HTTPS origin or explicit local development.");
  return url.origin + destination;
}

// Never render Supabase's raw URL fragment or upstream error description.
export function hunterAuthReturnError(fragment: string): string | null {
  const fields = new URLSearchParams(fragment.startsWith("#") ? fragment.slice(1) : fragment);
  if (!fields.has("error") && !fields.has("error_code")) return null;
  if (fields.get("error_code") === "otp_expired")
    return "This verification link expired or was already used. Request a new email link.";
  return "We could not complete verification with that link. Request a fresh email or retry your identity provider.";
}
