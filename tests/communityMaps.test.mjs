import test from "node:test";
import assert from "node:assert/strict";
import {
  browseCommunityMaps, communityApiUrl, communityServiceUrl,
  getCommunityMap, isMapHash, isMapId,
} from "../lib/communityMaps.ts";

const validHash = "a".repeat(64);
const mapId = "ffad8718-b3b8-4283-92e2-098a2f482116";

test("map identities are strict and unsuitable for URL path injection", () => {
  assert.equal(isMapId(mapId), true);
  assert.equal(isMapId(mapId + "/extra"), false);
  assert.equal(isMapId("../../maps"), false);
  assert.equal(isMapHash(validHash), true);
  assert.equal(isMapHash("A".repeat(64)), false);
  assert.equal(isMapHash(validHash + "/../"), false);
});

test("map service accepts HTTPS and localhost but not credentialed or remote HTTP URLs", () => {
  const saved = process.env.NEXT_PUBLIC_MAP_SERVICE_URL;
  try {
    process.env.NEXT_PUBLIC_MAP_SERVICE_URL = "https://maps.example.org/hub/";
    assert.equal(communityServiceUrl().origin, "https://maps.example.org");
    assert.equal(communityApiUrl("v2/maps"), "https://maps.example.org/hub/v2/maps");
    process.env.NEXT_PUBLIC_MAP_SERVICE_URL = "http://localhost:8091/hub/";
    assert.equal(communityServiceUrl().hostname, "localhost");
    process.env.NEXT_PUBLIC_MAP_SERVICE_URL = "http://maps.example.org/";
    assert.throws(() => communityServiceUrl(), /HTTPS/);
    process.env.NEXT_PUBLIC_MAP_SERVICE_URL = "https://name:pass@maps.example.org/";
    assert.throws(() => communityServiceUrl(), /credentials/);
  } finally {
    if (saved === undefined) delete process.env.NEXT_PUBLIC_MAP_SERVICE_URL;
    else process.env.NEXT_PUBLIC_MAP_SERVICE_URL = saved;
  }
});

test("public catalog respects authoritative page, search, sort and total count", async () => {
  const oldFetch = globalThis.fetch;
  const expected = {
    mapId,
    name: "TEST_ARENA",
    displayName: "Test Arena",
    latestRevision: { hash: validHash, package: { hash: validHash } },
  };
  globalThis.fetch = async (url, options) => {
    const parsed = new URL(url);
    assert.equal(parsed.pathname, "/v2/maps");
    assert.equal(parsed.searchParams.get("page"), "2");
    assert.equal(parsed.searchParams.get("pageSize"), "24");
    assert.equal(parsed.searchParams.get("query"), "arena");
    assert.equal(parsed.searchParams.get("sort"), "name");
    assert.equal(options.redirect, "error");
    return new Response(JSON.stringify([expected, { name: "invalid" }]), {
      status: 200,
      headers: { "X-Total-Count": "25", "Content-Type": "application/json" },
    });
  };
  try {
    const catalog = await browseCommunityMaps({ page: 2, query: "arena", sort: "name" });
    assert.equal(catalog.total, 25);
    assert.equal(catalog.items.length, 1);
    assert.equal(catalog.items[0].mapId, mapId);
  } finally { globalThis.fetch = oldFetch; }
});

test("invalid project IDs are rejected without outbound fetch", async () => {
  const oldFetch = globalThis.fetch;
  globalThis.fetch = () => { throw new Error("Must not fetch"); };
  try { assert.equal(await getCommunityMap("not-a-map-id"), null); }
  finally { globalThis.fetch = oldFetch; }
});
