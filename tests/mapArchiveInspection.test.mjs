import test from "node:test";
import assert from "node:assert/strict";
import { createRequire, registerHooks } from "node:module";
import { readFileSync } from "node:fs";
import { createHash } from "node:crypto";
import { BlobWriter, TextReader, ZipWriter } from "@zip.js/zip.js";

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

const { inspectMapArchive } = await import("../lib/mapArchiveInspection.ts");
const { inspectMapPackage } = await import("../lib/mapPublishing.ts");
const mapId = "00000000-0000-0000-0000-000000000001";
const manifest = JSON.stringify({
  format: 3, mapId, name: "Test Arena", contentHash: "a".repeat(64),
});

async function makeArchive(entries) {
  const writer = new ZipWriter(new BlobWriter("application/zip"));
  for (const [name, content, options] of entries) {
    await writer.add(name, new TextReader(content), options);
  }
  return await writer.close();
}

function asFile(blob, name) {
  return new File([blob], name, { type: "application/zip" });
}

test("valid package manifest wins over optional BSP entries", async () => {
  const file = asFile(await makeArchive([
    ["maps/q3dm17.bsp", "optional trimmed BSP"],
    ["manifest.json", manifest],
  ]), "arena.ppmap");
  const result = await inspectMapArchive(file);
  assert.equal(result.kind, "ppmap");
  assert.equal(result.manifest.mapId, mapId);
});

test("Project Prime inspection preserves the exact archive SHA-256", async () => {
  const blob = await makeArchive([["manifest.json", manifest]]);
  const file = asFile(blob, "arena.ppmap");
  const result = await inspectMapPackage(file, new AbortController().signal, () => {});
  const exactBytesHash = createHash("sha256")
    .update(new Uint8Array(await blob.arrayBuffer())).digest("hex");
  assert.equal(result.hash, exactBytesHash);
  assert.equal(result.manifest.mapId, mapId);
});

test("a manifest-free PK3 with playable BSP paths is structurally identified", async () => {
  const file = asFile(await makeArchive([
    ["maps/", ""],
    ["maps/q3dm17.bsp", "compiled level"],
    ["textures/arena/wall.tga", "texture"],
  ]), "arena.pk3");
  assert.deepEqual(await inspectMapArchive(file), {
    kind: "pk3", levels: ["q3dm17"],
  });
});

test("content and extension mismatches fail with per-file rename guidance", async () => {
  const ppmap = await makeArchive([["manifest.json", manifest]]);
  const pk3 = await makeArchive([["maps/q3dm17.bsp", "compiled level"]]);
  await assert.rejects(inspectMapArchive(asFile(ppmap, "misnamed.pk3")), /named \.pk3.*Rename it to \.ppmap/);
  await assert.rejects(inspectMapArchive(asFile(pk3, "misnamed.ppmap")), /named \.ppmap.*Rename it to \.pk3/);
});

test("an invalid root manifest never falls back to an otherwise playable PK3", async () => {
  const file = asFile(await makeArchive([
    ["maps/q3dm17.bsp", "compiled level"],
    ["manifest.json", "{not json"],
  ]), "bad.ppmap");
  await assert.rejects(inspectMapArchive(file), /manifest\.json is not valid JSON/);
});

test("ambiguous duplicate filenames and archives without either structure are rejected", async () => {
  const duplicate = await makeArchive([
    ["manifest.json", manifest],
    ["manifestXjson", manifest],
  ]);
  const bytes = new Uint8Array(await duplicate.arrayBuffer());
  const oldName = new TextEncoder().encode("manifestXjson");
  const newName = new TextEncoder().encode("manifest.json");
  let replacements = 0;
  for (let offset = 0; offset <= bytes.length - oldName.length; offset++) {
    if (oldName.every((byte, index) => bytes[offset + index] === byte)) {
      bytes.set(newName, offset);
      replacements++;
      offset += oldName.length - 1;
    }
  }
  assert.equal(replacements, 2);
  await assert.rejects(inspectMapArchive(asFile(new Blob([bytes]), "ambiguous.ppmap")));

  const unrelated = asFile(await makeArchive([["textures/wall.tga", "texture"]]), "unrelated.pk3");
  await assert.rejects(inspectMapArchive(unrelated), /No Project Prime manifest|No playable/);
});

test("unsafe PK3 paths and symbolic links are rejected", async () => {
  const pathFile = asFile(await makeArchive([
    ["../escape.txt", "outside"],
    ["maps/q3dm17.bsp", "compiled level"],
  ]), "unsafe.pk3");
  await assert.rejects(inspectMapArchive(pathFile));

  const linkFile = asFile(await makeArchive([
    ["maps/q3dm17.bsp", "compiled level"],
    ["textures/link", "../outside", { unixMode: 0o120777 }],
  ]), "linked.pk3");
  await assert.rejects(inspectMapArchive(linkFile), /unsafe archive entry/);
});

test("PK3 entries cannot declare expanded sizes above the existing per-entry limit", async () => {
  const blob = await makeArchive([["maps/q3dm17.bsp", "small source entry"]]);
  const bytes = new Uint8Array(await blob.arrayBuffer());
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  const centralDirectorySignature = [0x50, 0x4b, 0x01, 0x02];
  let found = false;
  for (let offset = 0; offset <= bytes.length - 46; offset++) {
    if (centralDirectorySignature.every((byte, index) => bytes[offset + index] === byte)) {
      view.setUint32(offset + 24, 128 * 1024 * 1024 + 1, true);
      found = true;
      break;
    }
  }
  assert.equal(found, true);
  await assert.rejects(inspectMapArchive(asFile(new Blob([bytes]), "oversized-entry.pk3")), /unsafe archive entry/);
});

test("malformed ZIPs and cancelled inspections are rejected", async () => {
  await assert.rejects(inspectMapArchive(new File(["not a zip"], "broken.pk3")));
  const controller = new AbortController();
  controller.abort();
  await assert.rejects(inspectMapArchive(new File(["not read"], "paused.pk3"), controller.signal),
    error => error?.name === "AbortError");
});

test("Project Prime package entry-count bound is enforced", async () => {
  const entries = [["manifest.json", manifest]];
  for (let index = 0; index < 2048; index++) entries.push([`assets/${index}.bin`, "x"]);
  const file = asFile(await makeArchive(entries), "large-index.ppmap");
  await assert.rejects(inspectMapArchive(file), /Too many package entries/);
});

test("chooser and drop share one queue path and one structural preflight", () => {
  const source = readFileSync(new URL("../components/maps/BulkMapUpload.tsx", import.meta.url), "utf8");
  assert.match(source, /function selectFiles[\s\S]*void addFiles\(chosen\)/);
  assert.match(source, /function handleDrop[\s\S]*void addFiles\(Array\.from\(event\.dataTransfer\.files\)\)/);
  assert.match(source, /const archive = await inspectMapArchive\(item\.source, signal\)/);
  assert.match(source, /inspectMapPackage\([\s\S]*archive\)/);
  assert.match(source, /scanPk3Levels\(item\.source, signal, archive\)/);
});
