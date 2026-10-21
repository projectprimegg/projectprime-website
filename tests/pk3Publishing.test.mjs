import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { getMapUploadKind } from "../lib/mapUploadFormats.ts";
import {
  MAX_PK3_SOURCE_BYTES, MAX_CONVERTED_PACKAGE_BYTES,
  PK3_UPLOAD_CHUNK_BYTES, pk3ConversionUrl, validPk3RoomName,
  validPk3Level, validConversionJobId,
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
test("conversion API explicitly requires HTTPS except local development", () => {
  const previous = process.env.NEXT_PUBLIC_PK3_CONVERSION_URL;
  try {
    delete process.env.NEXT_PUBLIC_PK3_CONVERSION_URL;
    assert.equal(pk3ConversionUrl(), null);
    process.env.NEXT_PUBLIC_PK3_CONVERSION_URL = "https://maps.example.test/pk3/";
    assert.equal(pk3ConversionUrl()?.pathname, "/pk3/");
    process.env.NEXT_PUBLIC_PK3_CONVERSION_URL = "http://localhost:8094/";
    assert.equal(pk3ConversionUrl()?.hostname, "localhost");
    process.env.NEXT_PUBLIC_PK3_CONVERSION_URL = "http://evil.example/";
    assert.throws(() => pk3ConversionUrl(), /HTTPS/);
    process.env.NEXT_PUBLIC_PK3_CONVERSION_URL = "https://user:pass@evil.example/";
    assert.throws(() => pk3ConversionUrl(), /credentials/);
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

test("main Maps upload shows exactly one file chooser and routes to existing handlers", () => {
  const mainPage = readFileSync(new URL("../app/maps/upload/page.tsx", import.meta.url), "utf8");
  const combined = readFileSync(new URL("../components/maps/MapUpload.tsx", import.meta.url), "utf8");
  const dedicatedPk3 = readFileSync(new URL("../app/maps/upload/pk3/page.tsx", import.meta.url), "utf8");

  assert.match(mainPage, /<MapUpload\s*\/>/);
  assert.doesNotMatch(mainPage, /<Pk3Upload/);
  assert.match(mainPage, /href="#map-upload"/);
  assert.equal((combined.match(/type="file"/g) || []).length, 1);
  assert.match(combined, /accept="\.ppmap,\.pk3"/);
  assert.match(combined, /getMapUploadKind\(selected\.name\)/);
  assert.match(combined, /inspectMapPackage\(selected,/);
  assert.match(combined, /scanPk3Levels\(selected\)/);
  assert.match(combined, /submitPk3Conversion\(/);
  assert.match(combined, /retrieveConvertedMap\(/);
  assert.match(combined, /publishMapPackage\(/);
  assert.match(combined, /rightsConfirmed/);
  assert.match(combined, /warningsReviewed/);
  assert.match(combined, /!converterConfigured/);
  assert.match(dedicatedPk3, /<Pk3Upload\s*\/>/);
});
