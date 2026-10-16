import test from "node:test";
import assert from "node:assert/strict";
import {
  BOARD_METRICS, BOARD_MODES, boardScore, browseHunterLeaderboard,
  getPublicHunter, isBoardMetric, isBoardMode, isPublicHunterId, sanitizeRank,
} from "../lib/publicHunters.ts";

const id = "a".repeat(32);
test("sort and mode filters are only supported contract keys", () => {
  assert.equal(isBoardMetric("wins"), true);
  assert.equal(isBoardMetric("rating"), false);
  assert.equal(isBoardMode("15"), true);
  assert.equal(isBoardMode("999"), false);
  assert.equal(BOARD_MODES.find(m => m.value === "15")?.label, "Insta-Gib");
  assert.equal(BOARD_METRICS.find(m => m.value === "headshots")?.label, "Headshots");
  assert.equal(isPublicHunterId(id), true);
  assert.equal(isPublicHunterId("a".repeat(36)), false);
});

test("private rank rows never reveal upstream names or any public UUID", () => {
  const sanitized = sanitizeRank({
    rank: 4, is_public: false, public_id: id,
    display_name: "Private Real Name", matches: 2, wins: 1,
    kills: 5, deaths: 1,
  });
  assert.equal(sanitized?.display_name, "Private Hunter");
  assert.equal(sanitized?.public_id, null);
  assert.equal(sanitized?.is_public, false);
  assert.equal(sanitized?.wins, 1);
});

test("opt-in profile link is retained only when public and opaque", () => {
  const publicRow = sanitizeRank({
    rank: 1, is_public: true, public_id: id,
    display_name: "Public Hunter", wins: 8, matches: 10,
    kills: 30, deaths: 20, win_rate: 80, kd: 1.5,
  });
  assert.equal(publicRow?.public_id, id);
  assert.equal(publicRow?.display_name, "Public Hunter");
  assert.equal(boardScore(publicRow, "win_rate"), "80.0%");
  assert.equal(boardScore(publicRow, "kd"), "1.50");
  const forged = sanitizeRank({ rank: 1, is_public: true,
    public_id: "invalid", display_name: "Forged", wins: 1 });
  assert.equal(forged?.display_name, "Private Hunter");
});

test("leaderboard request always uses bounded filters and validates origin data", async () => {
  const prior = globalThis.fetch;
  globalThis.fetch = async (url, options) => {
    const params = new URL(url).searchParams;
    assert.equal(params.get("metric"), "wins");
    assert.equal(params.get("mode"), "3");
    assert.equal(params.get("page"), "2");
    assert.equal(params.get("only_public"), "1");
    assert.equal(params.get("q"), "Samus");
    assert.equal(options.redirect, "error");
    return new Response(JSON.stringify({
      source: "trusted_career", page: 2, page_size: 25, total: 27,
      ranked_available: false,
      entries: [{ rank:26, is_public:false,
        public_id:id, display_name:"Hidden Name", matches:4, wins:3 }],
    }), { status:200, headers:{ "Content-Type":"application/json" } });
  };
  try {
    const data=await browseHunterLeaderboard({ metric:"rating",
      mode:"3", page:2, onlyPublic:true, search:"Samus" });
    assert.equal(data.total,27);
    assert.equal(data.entries[0].display_name,"Private Hunter");
    assert.equal(data.ranked_available,false);
  } finally { globalThis.fetch=prior; }
});

test("non-public profile response does not expose a Hunter detail card", async () => {
  const prior = globalThis.fetch;
  globalThis.fetch = async () => new Response(null,{status:404});
  try {
    assert.equal(await getPublicHunter(id),null);
    assert.equal(await getPublicHunter("../traversal"),null);
  } finally { globalThis.fetch=prior; }
});
