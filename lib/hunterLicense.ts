// The response is the existing game's HunterLicenseSnapshot JSON contract.
// The website never computes or posts authoritative career statistics.
export const HUNTERS = [
  "Samus", "Kanden", "Trace", "Sylux", "Noxus", "Spire", "Weavel",
] as const;

export const GAME_MODES: Record<number, string> = {
  3: "Battle", 4: "Battle Teams", 5: "Survival", 6: "Survival Teams",
  7: "Capture", 8: "Bounty", 9: "Bounty Teams", 10: "Nodes",
  11: "Nodes Teams", 12: "Defender", 13: "Defender Teams",
  14: "Prime Hunter", 15: "Insta-Gib", 16: "Relic",
  17: "Hardpoint", 18: "Hardpoint Teams", 19: "Gun Game",
  20: "Kill Confirmed", 21: "Kill Confirmed Teams",
  22: "Headhunter", 23: "One in the Chamber",
};

export type HunterLicenseProfile = {
  player_id: string;
  display_name: string;
  favorite_hunter: number;
  created_at: string | null;
  rating_points: number;
  rating_tier: number | null;
};
export type HunterLicenseStats = {
  games_played: number;
  wins: number;
  losses: number;
  ties: number;
  kills: number;
  deaths: number;
  assists: number;
  damage: number;
  played_ticks: number;
  headshots: number;
  longest_kill_streak: number;
  current_win_streak: number;
  longest_win_streak: number;
  octolith_scores: number;
  nodes_captured: number;
  kills_as_prime: number;
};
export type HunterLicenseMatch = {
  match_id: string;
  played_at: string | null;
  room_key: string;
  mode: number;
  trust_class: number;
  career_eligible: boolean;
  eligible: boolean;
  won: boolean;
  tied: boolean;
  kills: number;
  deaths: number;
  assists: number;
  damage: number;
};
export type HunterLicenseCosmetic = {
  hunter: number;
  skin_key: string;
  armor_effect_key: string;
  death_effect_key: string;
};
export type HunterLicenseSnapshot = {
  profile: HunterLicenseProfile;
  stats: HunterLicenseStats;
  matches: HunterLicenseMatch[];
  cosmetics: HunterLicenseCosmetic[];
};

export function isLicenseSnapshot(value: unknown): value is HunterLicenseSnapshot {
  if (!value || typeof value !== "object") return false;
  const snapshot = value as Partial<HunterLicenseSnapshot>;
  const p = snapshot.profile;
  const s = snapshot.stats;
  return !!p && typeof p.player_id === "string" &&
    /^[a-f\d-]{36}$/i.test(p.player_id) &&
    typeof p.display_name === "string" && typeof p.favorite_hunter === "number" &&
    !!s && Number.isFinite(s.games_played) && Number.isFinite(s.wins) &&
    Number.isFinite(s.kills) && Number.isFinite(s.deaths) &&
    Array.isArray(snapshot.matches) && Array.isArray(snapshot.cosmetics);
}
export function hunterName(id: number) { return HUNTERS[id] ?? "Unknown Hunter"; }
export function gameModeName(id: number) { return GAME_MODES[id] ?? "Mode " + id; }
export function winRate(wins: number, games: number) {
  return games > 0 ? (wins * 100 / games).toFixed(1) + "%" : "0.0%";
}
export function kdRatio(kills: number, deaths: number) {
  return deaths === 0 ? (kills > 0 ? "∞" : "0.00") : (kills / deaths).toFixed(2);
}
export function achieved(stats: HunterLicenseStats) {
  return [
    { title: "First Hunt", unlocked: stats.games_played >= 1 },
    { title: "First Victory", unlocked: stats.wins >= 1 },
    { title: "Centurion", unlocked: stats.kills >= 100 },
    { title: "Headhunter", unlocked: stats.headshots >= 50 },
    { title: "Ace", unlocked: stats.wins >= 25 },
    { title: "Veteran", unlocked: stats.games_played >= 50 },
    { title: "Legacy", unlocked: stats.games_played >= 250 },
  ];
}
