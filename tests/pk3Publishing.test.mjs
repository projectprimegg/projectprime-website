import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { getMapUploadKind, planMapBatch, MAX_BULK_MAPS } from "../lib/mapUploadFormats.ts";
import {
  MAX_PK3_SOURCE_BYTES, MAX_CONVERTED_PACKAGE_BYTES,
  PK3_UPLOAD_CHUNK_BYTES, pk3ConversionUrl, validPk3RoomName,
  validPk3Level, validConversionJobId, isPk3ConverterReady,
} from "../lib/pk3Contract.ts";

test("website PK3 upload/worker limits match standalone service", () => {
  assert.equal(MAX_PK3_SOURCE_BYTES, 128 * 1024 * 1024);
  assert.equal(MAX_CONVERTED_PACKAGE_BYTES, 512 * 1024 * 1024);
  assert.equal(PK3_UPLOAD_CHUNK_BYTES, 8 * 1024 * 1024);
});
test("map and BSP names reject paths, shell fragments and reserved Windows device names", () => {
  assert.equal(validPk3RoomName("Q3 WEB ARENA"), true);
  assert.equal(validPk3RoomName("../escape"), false);
  assert.equal(validPk3RoomName("CON"), false);
  assert.equal(validPk3RoomName("bad;rm"), false);
  assert.equal(validPk3RoomName("a".repeat(41)), false);
  assert.equal(validPk3Level("q3dm17"), true);
  assert.equal(validPk3Level(".hidden"), false);
  assert.equal(validPk3Level("../../secrets"), false);
  assert.equal(validConversionJobId("d4d3f188-b4b9-4f86-af38-d730822419ce"), true);
  assert.equal(validConversionJobId("pk3/../../../"), false);
});
test("PK3 converter defaults to Community HTTPS host but supports explicit override", () => {
  const beforePk3 = process.env.NEXT_PUBLIC_PK3_CONVERSION_URL;
  const beforeMap = process.env.NEXT_PUBLIC_MAP_SERVICE_URL;
  try {
    delete process.env.NEXT_PUBLIC_PK3_CONVERSION_URL;
    delete process.env.NEXT_PUBLIC_MAP_SERVICE_URL;
    assert.equal(pk3ConversionUrl().href, "https://maps.projectprime.gg/pk3/");
    process.env.NEXT_PUBLIC_MAP_SERVICE_URL = "https://maps.example.test/";
    assert.equal(pk3ConversionUrl().href, "https://maps.example.test/pk3/");
    process.env.NEXT_PUBLIC_PK3_CONVERSION_URL = "https://converter.example.test/source/";
    assert.equal(pk3ConversionUrl().href, "https://converter.example.test/source/");
    process.env.NEXT_PUBLIC_PK3_CONVERSION_URL = "http://localhost:8094/";
    assert.equal(pk3ConversionUrl().hostname, "localhost");
    process.env.NEXT_PUBLIC_PK3_CONVERSION_URL = "http://evil.example/";
    assert.throws(() => pk3ConversionUrl(), /HTTPS/);
    process.env.NEXT_PUBLIC_PK3_CONVERSION_URL = "https://user:pass@evil.example/";
    assert.throws(() => pk3ConversionUrl(), /credentials/);
    delete process.env.NEXT_PUBLIC_PK3_CONVERSION_URL;
    process.env.NEXT_PUBLIC_MAP_SERVICE_URL = "http://insecure.example/";
    assert.throws(() => pk3ConversionUrl(), /HTTPS/);
  } finally {
    if (beforePk3 == null) delete process.env.NEXT_PUBLIC_PK3_CONVERSION_URL;
    else process.env.NEXT_PUBLIC_PK3_CONVERSION_URL = beforePk3;
    if (beforeMap == null) delete process.env.NEXT_PUBLIC_MAP_SERVICE_URL;
    else process.env.NEXT_PUBLIC_MAP_SERVICE_URL = beforeMap;
  }
});

test("PK3 source upload requires the converter's specific healthy response", async () => {
  const previous = process.env.NEXT_PUBLIC_PK3_CONVERSION_URL;
  process.env.NEXT_PUBLIC_PK3_CONVERSION_URL = "https://converter.example.test/pk3/";
  try {
  let checked = "";
  const success = await isPk3ConverterReady(async (input, options) => {
    checked = String(input);
    assert.equal(options.method, "GET");
    assert.equal(options.cache, "no-store");
    assert.equal(options.redirect, "error");
    return new Response(JSON.stringify({ ok: true }), {
      status: 200, headers: { "content-type": "application/json" },
    });
  });
  assert.equal(success, true);
  assert.equal(new URL(checked).pathname.endsWith("/pk3/health"), true);

  assert.equal(await isPk3ConverterReady(async () =>
    new Response(JSON.stringify({ status: "ok" }), { status: 200 })), false);
  assert.equal(await isPk3ConverterReady(async () =>
    new Response("Not Found", { status: 404 })), false);
  assert.equal(await isPk3ConverterReady(async () =>
    new Response("<html>proxy fallback</html>", { status: 200 })), false);
  assert.equal(await isPk3ConverterReady(async () => {
    throw new TypeError("CORS blocked or endpoint offline");
  }), false);
  } finally {
    if (previous == null) delete process.env.NEXT_PUBLIC_PK3_CONVERSION_URL;
    else process.env.NEXT_PUBLIC_PK3_CONVERSION_URL = previous;
  }
});

test("main upload picker classifies both formats without accepting disguised files", () => {
  assert.equal(getMapUploadKind("arena.ppmap"), "ppmap");
  assert.equal(getMapUploadKind("Arena.PPMAP"), "ppmap");
  assert.equal(getMapUploadKind("q3dm17.pk3"), "pk3");
  assert.equal(getMapUploadKind("Q3DM17.PK3"), "pk3");
  assert.equal(getMapUploadKind("arena.pk3.exe"), null);
  assert.equal(getMapUploadKind("arena.ppmap.zip"), null);
  assert.equal(getMapUploadKind("arena.bsp"), null);
  assert.equal(getMapUploadKind(""), null);
});

test("main Maps upload uses one multi-select picker and preserves both workflows", () => {
  const mainPage = readFileSync(new URL("../app/maps/upload/page.tsx", import.meta.url), "utf8");
  const wrapper = readFileSync(new URL("../components/maps/MapUpload.tsx", import.meta.url), "utf8");
  const combined = readFileSync(new URL("../components/maps/BulkMapUpload.tsx", import.meta.url), "utf8");
  const dedicatedPk3 = readFileSync(new URL("../app/maps/upload/pk3/page.tsx", import.meta.url), "utf8");

  assert.match(mainPage, /<MapUpload\s*\/>/);
  assert.match(wrapper, /BulkMapUpload as MapUpload/);
  assert.equal((combined.match(/type="file"/g) || []).length, 1);
  assert.match(combined, /type="file" multiple accept="\.ppmap,\.pk3"/);
  assert.match(combined, /planMapBatch\(/);
  assert.match(combined, /inspectMapPackage\(/);
  assert.match(combined, /scanPk3Levels\(/);
  assert.match(combined, /submitPk3Conversion\(/);
  assert.match(combined, /retrieveConvertedMap\(/);
  assert.match(combined, /publishMapPackage\(/);
  assert.match(combined, /warningsReviewed/);
  assert.match(combined, /rightsConfirmed/);
  assert.match(combined, /usePk3ServiceAvailability/);
  assert.match(dedicatedPk3, /<Pk3Upload\s*\/>/);
});

test("bulk planner accepts mixed archives but rejects duplicates, oversize and nonmap files", () => {
  const file = (name, size, lastModified = 1) => ({ name, size, lastModified });
  const selected = planMapBatch([
    file("arena.ppmap", 1024), file("q3dm17.pk3", 2048),
    file("arena.ppmap", 1024), file("oversize.pk3", 128 * 1024 * 1024 + 1),
    file("evil.pk3.exe", 100), file("empty.ppmap", 0),
  ], []);
  assert.deepEqual(selected.accepted.map(a => a.kind), ["ppmap", "pk3"]);
  assert.equal(selected.rejected.length, 4);
  assert.match(selected.rejected[0].reason, /already in the queue/);
  assert.match(selected.rejected[1].reason, /128 MiB/);

  const existing = Array.from({ length: MAX_BULK_MAPS - 1 },
    (_, i) => file("map-" + i + ".ppmap", 100));
  const full = planMapBatch([file("one.ppmap", 10), file("two.pk3", 10)], existing);
  assert.equal(full.accepted.length, 1);
  assert.match(full.rejected[0].reason, /batch is full/);
});
