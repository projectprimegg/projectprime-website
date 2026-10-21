import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
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

test("primary Maps uploader embeds PK3 beneath PPMAP without duplicating sign-in", () => {
  const mainPage = readFileSync(new URL("../app/maps/upload/page.tsx", import.meta.url), "utf8");
  const pk3 = readFileSync(new URL("../components/maps/Pk3Upload.tsx", import.meta.url), "utf8");
  const ppmap = readFileSync(new URL("../components/maps/MapUpload.tsx", import.meta.url), "utf8");

  assert.match(mainPage, /<MapUpload\s*\/>/);
  assert.match(mainPage, /<Pk3Upload\s+embedded\s*\/>/);
  assert.ok(mainPage.indexOf("<MapUpload />") < mainPage.indexOf("<Pk3Upload embedded />"));
  assert.match(mainPage, /href="#pk3-upload"/);
  assert.match(ppmap, /id="ppmap-upload"/);
  assert.match(pk3, /accept="\.pk3"/);
  assert.match(pk3, /if \(!session\) return embedded/);
  assert.match(pk3, /href=\{embedded \? "#ppmap-upload" : "\/maps\/upload"\}/);
});
