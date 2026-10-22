import test from "node:test";
import assert from "node:assert/strict";
import { createRequire, registerHooks } from "node:module";
import { readFileSync } from "node:fs";

const require = createRequire(import.meta.url);
const ts = require("typescript");
registerHooks({
  resolve(specifier, context, nextResolve) {
    if (specifier.startsWith("./") && context.parentURL?.endsWith(".ts") &&
        !/\.[a-z]+$/i.test(specifier)) {
      return nextResolve(specifier + ".ts", context);
    }
    return nextResolve(specifier, context);
  },
  load(url, context, nextLoad) {
    if (url.startsWith("file:") && url.endsWith(".ts")) {
      const source = readFileSync(new URL(url), "utf8");
      const output = ts.transpileModule(source, {
        compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 },
      }).outputText;
      return { format: "module", source: output, shortCircuit: true };
    }
    return nextLoad(url, context);
  },
});

const { publishMapPackage } = await import("../lib/mapPublishing.ts");
const json = value => new Response(JSON.stringify(value), {
  headers: { "content-type": "application/json" },
});
const droppedJsonBody = () => new Response(new ReadableStream({
  start(controller) {
    controller.error(new TypeError("connection dropped while reading response body"));
  },
}), { status: 200, headers: { "content-type": "application/json" } });
const uuid = value => "00000000-0000-0000-0000-" + String(value).padStart(12, "0");
const hash = value => String(value).repeat(64);
const clientFor = ownerId => ({ auth: { getSession: async () => ({
  data: { session: { access_token: "jwt", user: { id: ownerId } } }, error: null,
}) } });
const mapFor = (contents, packageHash, mapId = uuid(9)) => ({
  file: new File([contents], "map.ppmap"),
  hash: packageHash,
  manifest: { format: 3, mapId, name: "Map", contentHash: packageHash },
});

async function publishForNetworkTest(map, signal) {
  return publishMapPackage({
    map, client: clientFor("owner-a"), visibility: "Published", notes: "",
    signal, progress() {},
  });
}

test("bulk publication reuses one owner catalog while minting per-map tickets", async t => {
  const originalFetch = globalThis.fetch;
  t.after(() => { globalThis.fetch = originalFetch; });
  let ticketCount = 0;
  let catalogCount = 0;
  const reservations = new Map();
  const starts = [];
  globalThis.fetch = async (input, init = {}) => {
    const url = String(input);
    const method = init.method || "GET";
    if (url === "/api/maps/ticket") {
      ticketCount++;
      return json({ ticket: "ticket-" + ticketCount });
    }
    if (url.includes("/v2/maps?mine=true")) {
      catalogCount++;
      return json([{ mapId: uuid(1), latestHash: hash("a") }]);
    }
    if (method === "POST" && url.includes("/uploads/") && !url.endsWith("/complete")) {
      const body = JSON.parse(init.body);
      const packageHash = url.split("/uploads/")[1];
      starts.push(body);
      reservations.set(packageHash, body.mapId);
      return json({ packageHash, bytes: body.bytes, offset: 0,
        chunkBytes: body.bytes, complete: false });
    }
    if (method === "PUT" && url.includes("/uploads/")) {
      const packageHash = url.split("/uploads/")[1].split("?")[0];
      const bytes = reservations.get(packageHash) ? 1 : 0;
      return json({ packageHash, bytes, offset: bytes,
        chunkBytes: bytes, complete: false });
    }
    if (method === "POST" && url.endsWith("/complete")) {
      const packageHash = url.split("/uploads/")[1].split("/")[0];
      return json({ hash: packageHash, mapId: reservations.get(packageHash),
        name: "Map", displayName: null, author: null, version: null, bytes: 1,
        supportedModes: [], minPlayers: 0, maxPlayers: 0, minimumProtocol: 0,
        favoriteCount: 0, publishedAt: "", listed: true, draft: false });
    }
    throw new Error("Unexpected request " + method + " " + url);
  };

  const client = { auth: { getSession: async () => ({
    data: { session: { access_token: "jwt", user: { id: "owner-a" } } }, error: null,
  }) } };
  const session = new (await import("../lib/mapPublishSession.ts")).MapPublishSession();
  const publish = async (mapId, packageHash) => {
    const file = new File([new Uint8Array([1])], "map.ppmap");
    return publishMapPackage({
      map: { file, hash: packageHash, manifest: {
        format: 3, mapId, name: "Map", contentHash: packageHash,
      } },
      client, visibility: "Published", notes: "", signal: new AbortController().signal,
      progress() {}, publishSession: session,
    });
  };

  await publish(uuid(1), hash("b"));
  await publish(uuid(2), hash("c"));
  await publish(uuid(1), hash("d"));

  assert.equal(ticketCount, 3);
  assert.equal(catalogCount, 1);
  assert.deepEqual(starts.map(item => [item.mapId, item.existingMap, item.expectedParentHash]), [
    [uuid(1), true, hash("a")],
    [uuid(2), false, null],
    [uuid(1), true, hash("b")],
  ]);
});

test("a dropped chunk is reconciled before sending only the unconfirmed suffix", async t => {
  const originalFetch = globalThis.fetch;
  t.after(() => { globalThis.fetch = originalFetch; });
  const contents = "abcdef";
  const map = mapFor(contents, hash("e"));
  const received = new Uint8Array(contents.length);
  const putOffsets = [];
  let serverOffset = 0;
  let putCount = 0;
  let recoveryGets = 0;
  globalThis.fetch = async (input, init = {}) => {
    const url = String(input);
    const method = init.method || "GET";
    if (url === "/api/maps/ticket") return json({ ticket: "ticket-a" });
    if (url.includes("/v2/maps?mine=true")) return json([]);
    if (method === "POST" && url.includes("/uploads/") && !url.endsWith("/complete")) {
      return json({ packageHash: map.hash, bytes: map.file.size, offset: 0,
        chunkBytes: 4, complete: false });
    }
    if (method === "PUT" && url.includes("/uploads/")) {
      const offset = Number(new URL(url).searchParams.get("offset"));
      const chunk = new Uint8Array(await init.body.arrayBuffer());
      putOffsets.push(offset);
      if (offset !== serverOffset) return json({ message: "offset conflict" }, 409);
      received.set(chunk, offset);
      serverOffset += chunk.length;
      putCount++;
      if (putCount === 1) throw new TypeError("connection dropped after acceptance");
      return json({ packageHash: map.hash, bytes: map.file.size, offset: serverOffset,
        chunkBytes: 4, complete: false });
    }
    if (method === "GET" && url.includes("/uploads/")) {
      recoveryGets++;
      return json({ packageHash: map.hash, bytes: map.file.size,
        offset: serverOffset, chunkBytes: 4, complete: false });
    }
    if (method === "POST" && url.endsWith("/complete")) {
      return json({ hash: map.hash, mapId: map.manifest.mapId });
    }
    throw new Error("Unexpected request " + method + " " + url);
  };

  await publishForNetworkTest(map, new AbortController().signal);
  assert.deepEqual(putOffsets, [0, 4]);
  assert.equal(recoveryGets, 1);
  assert.deepEqual(received, new Uint8Array(new TextEncoder().encode(contents)));
});

test("a dropped successful PUT body is reconciled before sending only the suffix", async t => {
  const originalFetch = globalThis.fetch;
  t.after(() => { globalThis.fetch = originalFetch; });
  const contents = "abcdef";
  const map = mapFor(contents, hash("c"));
  const received = new Uint8Array(contents.length);
  const putOffsets = [];
  let serverOffset = 0;
  let recoveryGets = 0;
  globalThis.fetch = async (input, init = {}) => {
    const url = String(input);
    const method = init.method || "GET";
    if (url === "/api/maps/ticket") return json({ ticket: "ticket-a" });
    if (url.includes("/v2/maps?mine=true")) return json([]);
    if (method === "POST" && url.includes("/uploads/") && !url.endsWith("/complete")) {
      return json({ packageHash: map.hash, bytes: map.file.size, offset: 0,
        chunkBytes: 4, complete: false });
    }
    if (method === "PUT" && url.includes("/uploads/")) {
      const offset = Number(new URL(url).searchParams.get("offset"));
      const chunk = new Uint8Array(await init.body.arrayBuffer());
      putOffsets.push(offset);
      if (offset !== serverOffset) return json({ message: "offset conflict" }, 409);
      received.set(chunk, offset);
      serverOffset += chunk.length;
      if (offset === 0) return droppedJsonBody();
      return json({ packageHash: map.hash, bytes: map.file.size, offset: serverOffset,
        chunkBytes: 4, complete: false });
    }
    if (method === "GET" && url.includes("/uploads/")) {
      recoveryGets++;
      return json({ packageHash: map.hash, bytes: map.file.size,
        offset: serverOffset, chunkBytes: 4, complete: false });
    }
    if (method === "POST" && url.endsWith("/complete")) {
      return json({ hash: map.hash, mapId: map.manifest.mapId });
    }
    throw new Error("Unexpected request " + method + " " + url);
  };

  await publishForNetworkTest(map, new AbortController().signal);
  assert.deepEqual(putOffsets, [0, 4]);
  assert.equal(recoveryGets, 1);
  assert.deepEqual(received, new Uint8Array(new TextEncoder().encode(contents)));
});

test("a dropped recovery GET body is retried before resuming at the confirmed offset", async t => {
  const originalFetch = globalThis.fetch;
  t.after(() => { globalThis.fetch = originalFetch; });
  const contents = "abcdef";
  const map = mapFor(contents, hash("b"));
  const received = new Uint8Array(contents.length);
  const putOffsets = [];
  let serverOffset = 0;
  let recoveryGets = 0;
  globalThis.fetch = async (input, init = {}) => {
    const url = String(input);
    const method = init.method || "GET";
    if (url === "/api/maps/ticket") return json({ ticket: "ticket-a" });
    if (url.includes("/v2/maps?mine=true")) return json([]);
    if (method === "POST" && url.includes("/uploads/") && !url.endsWith("/complete")) {
      return json({ packageHash: map.hash, bytes: map.file.size, offset: 0,
        chunkBytes: 4, complete: false });
    }
    if (method === "PUT" && url.includes("/uploads/")) {
      const offset = Number(new URL(url).searchParams.get("offset"));
      const chunk = new Uint8Array(await init.body.arrayBuffer());
      putOffsets.push(offset);
      if (offset !== serverOffset) return json({ message: "offset conflict" }, 409);
      received.set(chunk, offset);
      serverOffset += chunk.length;
      if (offset === 0) throw new TypeError("connection dropped after acceptance");
      return json({ packageHash: map.hash, bytes: map.file.size, offset: serverOffset,
        chunkBytes: 4, complete: false });
    }
    if (method === "GET" && url.includes("/uploads/")) {
      recoveryGets++;
      if (recoveryGets === 1) return droppedJsonBody();
      return json({ packageHash: map.hash, bytes: map.file.size,
        offset: serverOffset, chunkBytes: 4, complete: false });
    }
    if (method === "POST" && url.endsWith("/complete")) {
      return json({ hash: map.hash, mapId: map.manifest.mapId });
    }
    throw new Error("Unexpected request " + method + " " + url);
  };

  await publishForNetworkTest(map, new AbortController().signal);
  assert.deepEqual(putOffsets, [0, 4]);
  assert.equal(recoveryGets, 2);
  assert.deepEqual(received, new Uint8Array(new TextEncoder().encode(contents)));
});

test("malformed JSON and invalid successful bodies fail without recovery", async t => {
  const originalFetch = globalThis.fetch;
  t.after(() => { globalThis.fetch = originalFetch; });

  for (const bodyType of ["malformed", "consumed", "invalid-state"]) {
    const map = mapFor("data", hash(bodyType === "malformed" ? "a" :
      bodyType === "consumed" ? "9" : "8"));
    let recoveryGets = 0;
    globalThis.fetch = async (input, init = {}) => {
      const url = String(input);
      const method = init.method || "GET";
      if (url === "/api/maps/ticket") return json({ ticket: "ticket-a" });
      if (url.includes("/v2/maps?mine=true")) return json([]);
      if (method === "POST" && url.includes("/uploads/") && !url.endsWith("/complete")) {
        return json({ packageHash: map.hash, bytes: map.file.size, offset: 0,
          chunkBytes: map.file.size, complete: false });
      }
      if (method === "PUT" && url.includes("/uploads/")) {
        if (bodyType === "malformed") return new Response("not json", { status: 200 });
        if (bodyType === "invalid-state") return json(null);
        const response = json({ packageHash: map.hash, bytes: map.file.size,
          offset: map.file.size, chunkBytes: map.file.size, complete: false });
        await response.json();
        return response;
      }
      if (method === "GET" && url.includes("/uploads/")) {
        recoveryGets++;
        return json({ packageHash: map.hash, bytes: map.file.size,
          offset: map.file.size, chunkBytes: map.file.size, complete: false });
      }
      throw new Error("Unexpected request " + method + " " + url);
    };

    if (bodyType === "malformed") {
      await assert.rejects(publishForNetworkTest(map, new AbortController().signal),
        SyntaxError);
    } else if (bodyType === "invalid-state") {
      await assert.rejects(publishForNetworkTest(map, new AbortController().signal),
        /invalid upload offset/);
    } else {
      await assert.rejects(publishForNetworkTest(map, new AbortController().signal),
        /unreadable upload state/);
    }
    assert.equal(recoveryGets, 0);
  }
});

test("repeated PUT and recovery GET network failures stop at the bounded retry limit", async t => {
  const originalFetch = globalThis.fetch;
  const originalRandom = Math.random;
  t.after(() => { globalThis.fetch = originalFetch; Math.random = originalRandom; });
  Math.random = () => 0;
  const map = mapFor("data", hash("f"));
  let putCount = 0;
  let recoveryGets = 0;
  globalThis.fetch = async (input, init = {}) => {
    const url = String(input);
    const method = init.method || "GET";
    if (url === "/api/maps/ticket") return json({ ticket: "ticket-a" });
    if (url.includes("/v2/maps?mine=true")) return json([]);
    if (method === "POST" && url.includes("/uploads/") && !url.endsWith("/complete")) {
      return json({ packageHash: map.hash, bytes: map.file.size, offset: 0,
        chunkBytes: map.file.size, complete: false });
    }
    if (method === "PUT" && url.includes("/uploads/")) {
      putCount++;
      throw new TypeError("offline");
    }
    if (method === "GET" && url.includes("/uploads/")) {
      recoveryGets++;
      throw new TypeError("offline");
    }
    throw new Error("Unexpected request " + method + " " + url);
  };

  await assert.rejects(publishForNetworkTest(map, new AbortController().signal),
    /connection failed repeatedly and upload progress could not be confirmed/);
  assert.equal(putCount, 1);
  assert.equal(recoveryGets, 5);
});

test("cancelling during the network-recovery wait prevents a state retry", async t => {
  const originalFetch = globalThis.fetch;
  const originalRandom = Math.random;
  t.after(() => { globalThis.fetch = originalFetch; Math.random = originalRandom; });
  Math.random = () => 0;
  const controller = new AbortController();
  const map = mapFor("data", hash("d"));
  let recoveryGets = 0;
  globalThis.fetch = async (input, init = {}) => {
    const url = String(input);
    const method = init.method || "GET";
    if (url === "/api/maps/ticket") return json({ ticket: "ticket-a" });
    if (url.includes("/v2/maps?mine=true")) return json([]);
    if (method === "POST" && url.includes("/uploads/") && !url.endsWith("/complete")) {
      return json({ packageHash: map.hash, bytes: map.file.size, offset: 0,
        chunkBytes: map.file.size, complete: false });
    }
    if (method === "PUT" && url.includes("/uploads/")) {
      setTimeout(() => controller.abort(), 10);
      throw new TypeError("connection dropped");
    }
    if (method === "GET" && url.includes("/uploads/")) {
      recoveryGets++;
      return json({ packageHash: map.hash, bytes: map.file.size, offset: 0,
        chunkBytes: map.file.size, complete: false });
    }
    throw new Error("Unexpected request " + method + " " + url);
  };

  await assert.rejects(publishForNetworkTest(map, controller.signal), { name: "AbortError" });
  assert.equal(recoveryGets, 0);
});
