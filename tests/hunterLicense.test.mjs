import test from "node:test";
import assert from "node:assert/strict";
import {
  HUNTERS, hunterName, gameModeName, kdRatio, winRate, achieved,
  isLicenseSnapshot,
} from "../lib/hunterLicense.ts";

const snapshot = {
  profile: {
    player_id: "16383ef6-5f5c-478c-b49a-bba3c7010e88",
    display_name: "Test Hunter",
    favorite_hunter: 2,
    created_at: null,
    rating_points: 0,
    rating_tier: null,
  },
  stats: {
    games_played: 4, wins: 2, losses: 1, ties: 1,
    kills: 40, deaths: 20, assists: 5, damage: 200,
    played_ticks: 3600, headshots: 2, longest_kill_streak: 3,
    current_win_streak: 1, longest_win_streak: 2,
    octolith_scores: 0, nodes_captured: 0, kills_as_prime: 0,
  },
  matches: [], cosmetics: [],
};

test("Hunter License snapshot is recognized but untrusted input rejected", () => {
  assert.equal(isLicenseSnapshot(snapshot), true);
  assert.equal(isLicenseSnapshot({ ...snapshot, profile: undefined }), false);
  assert.equal(isLicenseSnapshot({ ...snapshot, stats: { kills: "999" } }), false);
  assert.equal(isLicenseSnapshot({ ...snapshot, matches: "not an array" }), false);
});
test("Hunter IDs match native enum order", () => {
  assert.deepEqual(HUNTERS, ["Samus", "Kanden", "Trace", "Sylux", "Noxus", "Spire", "Weavel"]);
  assert.equal(hunterName(2), "Trace");
  assert.equal(hunterName(99), "Unknown Hunter");
});
test("career display does not fabricate rating or incorrect match mode", () => {
  assert.equal(gameModeName(3), "Battle");
  assert.equal(gameModeName(15), "Insta-Gib");
  assert.equal(gameModeName(99), "Mode 99");
  assert.equal(winRate(2, 4), "50.0%");
  assert.equal(winRate(0, 0), "0.0%");
  assert.equal(kdRatio(10, 0), "∞");
  assert.equal(kdRatio(0, 0), "0.00");
  assert.equal(kdRatio(10, 5), "2.00");
});
test("milestones derive only from accepted private totals", () => {
  const titles = achieved(snapshot.stats).filter(a => a.unlocked).map(a => a.title);
  assert.deepEqual(titles, ["First Hunt", "First Victory"]);
});
