// Public, server-rendered career read model. This is NOT the private
// Hunter License snapshot and never includes Supabase/Auth account UUIDs.
export const BOARD_METRICS = [
  { value: "wins", label: "Wins" },
  { value: "kills", label: "Kills" },
  { value: "matches", label: "Matches" },
  { value: "kd", label: "K/D Ratio" },
  { value: "win_rate", label: "Win Rate" },
  { value: "headshots", label: "Headshots" },
] as const;
export type BoardMetric = typeof BOARD_METRICS[number]["value"];

export const BOARD_MODES = [
  { value: "all", label: "All Eligible Modes" },
  { value: "3", label: "Battle FFA" },
  { value: "4", label: "Battle Teams" },
  { value: "5", label: "Survival" },
  { value: "6", label: "Survival Teams" },
  { value: "7", label: "Capture" },
  { value: "8", label: "Bounty" },
  { value: "9", label: "Bounty Teams" },
  { value: "10", label: "Nodes" },
  { value: "11", label: "Nodes Teams" },
  { value: "12", label: "Defender" },
  { value: "13", label: "Defender Teams" },
  { value: "14", label: "Prime Hunter" },
  { value: "15", label: "Insta-Gib" },
  { value: "16", label: "Relic" },
  { value: "17", label: "Hardpoint" },
  { value: "18", label: "Hardpoint Teams" },
  { value: "19", label: "Gun Game" },
  { value: "20", label: "Kill Confirmed" },
  { value: "21", label: "Kill Confirmed Teams" },
  { value: "22", label: "Headhunter" },
  { value: "23", label: "One in the Chamber" },
] as const;
export type BoardMode = typeof BOARD_MODES[number]["value"];

export type PublicHunterRank = {
  rank: number;
  public_id: string | null;
  display_name: string;
  is_public: boolean;
  matches: number;
  wins: number;
  losses: number;
  ties: number;
  kills: number;
  deaths: number;
  assists: number;
  headshots: number;
  kd: number;
  win_rate: number;
};
export type Leaderboard = {
  version: number;
  source: "trusted_career";
  metric: BoardMetric;
  mode: BoardMode;
  page: number;
  page_size: number;
  total: number;
  ranked_available: boolean;
  entries: PublicHunterRank[];
};
export type PublicHunterProfile = Omit<PublicHunterRank, "rank" | "is_public"> & {
  public_id: string;
  favorite_hunter: number;
  damage: number;
  octolith_scores: number;
  nodes_captured: number;
};

export function isBoardMetric(value: unknown): value is BoardMetric {
  return typeof value === "string" && BOARD_METRICS.some(x => x.value === value);
}
export function isBoardMode(value: unknown): value is BoardMode {
  return typeof value === "string" && BOARD_MODES.some(x => x.value === value);
}
export function isPublicHunterId(value: unknown): value is string {
  return typeof value === "string" && /^[0-9a-f]{32}$/.test(value);
}

function cleanNumber(value: unknown) {
  return typeof value === "number" && Number.isFinite(value) && value >= 0 ? value : 0;
}
export function sanitizeRank(row: unknown): PublicHunterRank | null {
  if (!row || typeof row !== "object") return null;
  const r = row as Partial<PublicHunterRank>;
  if (!Number.isInteger(r.rank) || !r.rank || r.rank < 1) return null;
  const exposed = r.is_public === true &&
    isPublicHunterId(r.public_id) &&
    typeof r.display_name === "string" &&
    r.display_name.trim().length > 0;
  return {
    rank: r.rank,
    public_id: exposed ? r.public_id! : null,
    display_name: exposed ? r.display_name!.slice(0, 48) : "Private Hunter",
    is_public: exposed,
    matches: cleanNumber(r.matches), wins: cleanNumber(r.wins),
    losses: cleanNumber(r.losses), ties: cleanNumber(r.ties),
    kills: cleanNumber(r.kills), deaths: cleanNumber(r.deaths),
    assists: cleanNumber(r.assists), headshots: cleanNumber(r.headshots),
    kd: cleanNumber(r.kd), win_rate: cleanNumber(r.win_rate),
  };
}

export function boardScore(row: PublicHunterRank, metric: BoardMetric) {
  if (metric === "kd") return row.kd.toFixed(2);
  if (metric === "win_rate") return row.win_rate.toFixed(1) + "%";
  return row[metric].toLocaleString("en-US");
}

function apiUrl(path: string) {
  const base = process.env.NEXT_PUBLIC_SUPABASE_URL ||
    "https://hwcjaygoistufktorbmf.supabase.co";
  const url = new URL(base);
  if (url.protocol !== "https:" || url.username || url.password ||
    url.search || url.hash) throw new Error("Invalid Supabase service URL.");
  return new URL(path, url.origin);
}
const publishableKey = process.env.NEXT_PUBLIC_SUPABASE_PUBLISHABLE_KEY ||
  "sb_publishable_EVT45OPl638kA_j8vZ0ebg_sw3aWaVz";

async function getResponse(url: URL) {
  // Visibility opt-outs must take effect on the very next request,
  // including leaderboard names and directory results. Never cache a
  // formerly public name after its owner has chosen to hide it.
  return fetch(url, {
    headers: { apikey: publishableKey, Accept: "application/json" },
    redirect: "error",
    cache: "no-store",
    signal: AbortSignal.timeout(12000),
  });
}

export async function browseHunterLeaderboard(options: {
  metric?: string;
  mode?: string;
  page?: number;
  search?: string;
  onlyPublic?: boolean;
} = {}): Promise<Leaderboard> {
  const metric = isBoardMetric(options.metric) ? options.metric : "wins";
  const mode = isBoardMode(options.mode) ? options.mode : "all";
  const page = Math.max(1, Math.min(100, Math.floor(options.page || 1)));
  const params = new URLSearchParams({ metric, mode, page: String(page) });
  if (options.search) params.set("q", options.search.slice(0, 40));
  if (options.onlyPublic) params.set("only_public", "1");
  const response = await getResponse(apiUrl("/functions/v1/hunter-leaderboard?" + params));
  if (!response.ok) throw new Error("Hunter career service returned " + response.status);
  const data: unknown = await response.json();
  if (!data || typeof data !== "object") throw new Error("Invalid leaderboard response.");
  const result = data as Partial<Leaderboard>;
  if (result.source !== "trusted_career" || !Array.isArray(result.entries) ||
    !Number.isFinite(result.total) || !Number.isFinite(result.page) ||
    !Number.isFinite(result.page_size)) throw new Error("Invalid leaderboard contract.");
  return {
    version: 1, source: "trusted_career",
    metric, mode, page,
    page_size: 25,
    total: cleanNumber(result.total),
    ranked_available: result.ranked_available === true,
    entries: result.entries.map(sanitizeRank).filter((r): r is PublicHunterRank => r !== null),
  };
}

export async function getPublicHunter(publicId: string): Promise<PublicHunterProfile | null> {
  if (!isPublicHunterId(publicId)) return null;
  const response = await getResponse(
    apiUrl("/functions/v1/hunter-leaderboard?hunter=" + publicId));
  if (response.status === 404) return null;
  if (!response.ok) throw new Error("Hunter career service returned " + response.status);
  const raw: unknown = await response.json();
  if (!raw || typeof raw !== "object") throw new Error("Invalid Hunter profile.");
  const value = raw as Partial<PublicHunterProfile>;
  if (value.public_id !== publicId || typeof value.display_name !== "string" ||
    !Number.isFinite(value.matches) || typeof value.favorite_hunter !== "number") {
    throw new Error("Unexpected public Hunter response.");
  }
  return {
    public_id: publicId,
    display_name: value.display_name.slice(0, 48),
    favorite_hunter: value.favorite_hunter,
    matches: cleanNumber(value.matches), wins: cleanNumber(value.wins),
    losses: cleanNumber(value.losses), ties: cleanNumber(value.ties),
    kills: cleanNumber(value.kills), deaths: cleanNumber(value.deaths),
    assists: cleanNumber(value.assists), headshots: cleanNumber(value.headshots),
    kd: cleanNumber(value.kd), win_rate: cleanNumber(value.win_rate),
    damage: cleanNumber(value.damage),
    octolith_scores: cleanNumber(value.octolith_scores),
    nodes_captured: cleanNumber(value.nodes_captured),
  };
}
