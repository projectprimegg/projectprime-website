import test from "node:test";
import assert from "node:assert/strict";
import { MapPublishSession } from "../lib/mapPublishSession.ts";

test("a batch loads one creator catalog and tracks its successful publications", async () => {
  const session = new MapPublishSession();
  let loads = 0;
  const load = async () => {
    loads++;
    return new Map([["map-a", "a".repeat(64)]]);
  };

  assert.equal(await session.latestHashFor("owner-a", "MAP-A", load), "a".repeat(64));
  assert.equal(await session.latestHashFor("owner-a", "map-b", load), null);
  assert.equal(loads, 1);

  session.recordPublished("owner-a", "map-b", "b".repeat(64));
  assert.equal(await session.latestHashFor("owner-a", "MAP-B", load), "b".repeat(64));
  session.recordPublished("owner-a", "map-a", "c".repeat(64));
  assert.equal(await session.latestHashFor("owner-a", "map-a", load), "c".repeat(64));
  assert.equal(loads, 1);
});

test("catalog snapshots are isolated by owner and reloaded after a failed publication", async () => {
  const session = new MapPublishSession();
  let loads = 0;
  const load = async () => new Map([["map-a", String(++loads)]]);

  assert.equal(await session.latestHashFor("owner-a", "map-a", load), "1");
  assert.equal(await session.latestHashFor("owner-b", "map-a", load), "2");
  assert.equal(await session.latestHashFor("owner-a", "map-a", load), "3");
  session.invalidate();
  assert.equal(await session.latestHashFor("owner-a", "map-a", load), "4");
  session.invalidate();
  assert.equal(await session.latestHashFor("owner-a", "map-a", load), "5");
  assert.equal(loads, 5);
});
