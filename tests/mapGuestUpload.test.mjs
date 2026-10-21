import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { ensureMapUploadIdentity } from "../lib/mapGuestUpload.ts";

test("registered sessions publish without creating disposable guest users", async () => {
  let checked = 0;
  let created = 0;
  const client = { auth: {
    getSession: async () => { checked++; return { data: { session: null } }; },
    signInAnonymously: async () => { created++; return { data: { session: null } }; },
  } };
  await ensureMapUploadIdentity(client, { user: { id: "registered" } });
  assert.equal(checked, 0);
  assert.equal(created, 0);
});

test("an updated authenticated session takes priority over guest creation", async () => {
  let created = 0;
  const client = { auth: {
    getSession: async () => ({ data: { session: { user: { id: "new-login" } } } }),
    signInAnonymously: async () => { created++; return { data: { session: null } }; },
  } };
  await ensureMapUploadIdentity(client, null);
  assert.equal(created, 0);
});

test("signed-out visitors create exactly one guest session on explicit submission", async () => {
  let checked = 0;
  let created = 0;
  const client = { auth: {
    getSession: async () => { checked++; return { data: { session: null } }; },
    signInAnonymously: async () => {
      created++;
      return { data: { session: { user: { id: "guest", is_anonymous: true } } }, error: null };
    },
  } };
  await ensureMapUploadIdentity(client, null);
  assert.equal(checked, 1);
  assert.equal(created, 1);
});

test("guest sign-in failures never fall back to an unowned map upload", async () => {
  const auth = {
    getSession: async () => ({ data: { session: null } }),
    signInAnonymously: async () => ({ data: { session: null }, error: new Error("Rate limited") }),
  };
  await assert.rejects(() => ensureMapUploadIdentity({ auth }, null), /Guest uploads are temporarily unavailable/);
  auth.signInAnonymously = async () => ({ data: { session: null }, error: null });
  await assert.rejects(() => ensureMapUploadIdentity({ auth }, null), /Guest uploads are temporarily unavailable/);
  auth.getSession = async () => ({ data: { session: null }, error: new Error("Can't check current auth") });
  await assert.rejects(() => ensureMapUploadIdentity({ auth }, null), /Could not check your current session/);
});

test("bulk guest uploads keep scoped ownership and do not bypass creator tickets", () => {
  const entry = readFileSync(new URL("../components/maps/MapUpload.tsx", import.meta.url), "utf8");
  const upload = readFileSync(new URL("../components/maps/BulkMapUpload.tsx", import.meta.url), "utf8");
  const account = readFileSync(new URL("../components/maps/MapAccount.tsx", import.meta.url), "utf8");
  const tickets = readFileSync(new URL("../app/api/maps/ticket/route.ts", import.meta.url), "utf8");

  assert.match(entry, /BulkMapUpload as MapUpload/);
  assert.match(upload, /multiple accept="\.ppmap,\.pk3"/);
  assert.match(upload, /await ensureMapUploadIdentity\(client, session\)/);
  assert.match(upload, /"Publish \/ Convert as Guest"/);
  assert.match(upload, /client, visibility, notes/);
  assert.match(upload, /item\.rightsConfirmed/);
  assert.match(upload, /item\.warningsReviewed/);
  assert.match(upload, /for \(const original of \[\.\.\.queueRef\.current\]\)/);
  assert.match(account, /Continue as Guest/);
  assert.doesNotMatch(account, /NEXT_PUBLIC_ENABLE_GUEST_PUBLISHING/);
  assert.match(tickets, /"authorization"/);
  assert.match(tickets, /Bearer /);
});
