import test from "node:test";
import assert from "node:assert/strict";
import {
  mapUploadNetworkRetryDelay, mapUploadRetryDelay, waitForMapUploadRetry,
} from "../lib/mapUploadRetry.ts";

const responseWithRetryAfter = value => new Response(null, {
  headers: value == null ? {} : { "Retry-After": value },
});

test("retry delay honors numeric and HTTP-date Retry-After values with a bound", () => {
  const now = Date.parse("Wed, 21 Oct 2026 07:28:00 GMT");
  assert.equal(mapUploadRetryDelay(responseWithRetryAfter("2.5"), 1, now), 2500);
  assert.equal(mapUploadRetryDelay(responseWithRetryAfter("Wed, 21 Oct 2026 07:28:05 GMT"), 1, now), 5000);
  assert.equal(mapUploadRetryDelay(responseWithRetryAfter("Wed, 21 Oct 2026 07:27:00 GMT"), 1, now), 0);
  assert.equal(mapUploadRetryDelay(responseWithRetryAfter("120"), 1, now), 30_000);
});

test("invalid retry headers retain the bounded exponential fallback", () => {
  assert.equal(mapUploadRetryDelay(responseWithRetryAfter("tomorrow"), 1), 200);
  assert.equal(mapUploadRetryDelay(responseWithRetryAfter(null), 8), 3000);
});

test("network recovery backoff grows exponentially, jitters and remains bounded", () => {
  assert.equal(mapUploadNetworkRetryDelay(1, () => 0), 100);
  assert.equal(mapUploadNetworkRetryDelay(2, () => 0.5), 300);
  assert.equal(mapUploadNetworkRetryDelay(20, () => 1), 3000);
  assert.equal(mapUploadNetworkRetryDelay(3, () => -1), 400);
});

test("pause cancels a pending retry immediately", async () => {
  const controller = new AbortController();
  const pending = waitForMapUploadRetry(30_000, controller.signal);
  controller.abort();
  await assert.rejects(pending, { name: "AbortError", message: "Upload paused." });
});

test("an already-paused upload does not schedule a retry", async () => {
  const controller = new AbortController();
  controller.abort();
  await assert.rejects(waitForMapUploadRetry(30_000, controller.signal), {
    name: "AbortError", message: "Upload paused.",
  });
});
