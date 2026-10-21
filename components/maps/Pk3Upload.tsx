"use client";

import Link from "next/link";
import { useEffect, useRef, useState, type ChangeEvent, type FormEvent } from "react";
import { MapAccountPanel, useMapAuth } from "@/components/maps/MapAccount";
import {
  publishMapPackage, type TransferProgress, type Visibility,
} from "@/lib/mapPublishing";
import {
  MAX_PK3_SOURCE_BYTES, pk3ConversionUrl, retrieveConvertedMap,
  scanPk3Levels, submitPk3Conversion, validPk3RoomName,
  type ConvertedPk3Job,
} from "@/lib/pk3Publishing";

function suggestedRoom(file: File): string {
  const name = file.name.replace(/\.pk3$/i, "").toUpperCase()
    .replace(/[^A-Z0-9 _-]/g, "_").replace(/^[_ -]+/, "");
  return "Q3_" + (name || "CUSTOM").slice(0, 36);
}

export function Pk3Upload({ embedded = false }: { embedded?: boolean } = {}) {
  const { client, session, loading } = useMapAuth();
  const controller = useRef<AbortController | null>(null);
  const [file, setFile] = useState<File | null>(null);
  const [levels, setLevels] = useState<string[]>([]);
  const [level, setLevel] = useState("");
  const [roomName, setRoomName] = useState("");
  const [rights, setRights] = useState(false);
  const [reviewed, setReviewed] = useState(false);
  const [visibility, setVisibility] = useState<Visibility>("Published");
  const [notes, setNotes] = useState("");
  const [phase, setPhase] = useState("idle");
  const [progress, setProgress] = useState<TransferProgress | null>(null);
  const [job, setJob] = useState<ConvertedPk3Job | null>(null);
  const [publishedId, setPublishedId] = useState<string | null>(null);
  const [message, setMessage] = useState("");

  const online = Boolean(process.env.NEXT_PUBLIC_PK3_CONVERSION_URL);
  useEffect(() => () => controller.current?.abort(), []);
  const busy = ["scanning", "starting", "hashing", "uploading", "converting",
    "retrieving", "inspecting", "publishing"].includes(phase);
  const percent = progress?.total
    ? Math.floor(Math.min(100, Math.max(0, progress.done * 100 / progress.total))) : 0;

  async function onChoose(event: ChangeEvent<HTMLInputElement>) {
    const selected = event.target.files?.[0];
    if (!selected || busy) return;
    controller.current?.abort();
    setFile(null); setLevels([]); setLevel(""); setMessage("");
    setJob(null); setReviewed(false); setPublishedId(null);
    setPhase("scanning");
    try {
      if (selected.size > MAX_PK3_SOURCE_BYTES)
        throw new Error("PK3 archive exceeds the 128 MiB website upload limit.");
      const found = await scanPk3Levels(selected);
      setFile(selected);
      setLevels(found); setLevel(found[0]);
      setRoomName(suggestedRoom(selected));
      setPhase("ready");
    } catch (error) {
      setPhase("error");
      setMessage(error instanceof Error ? error.message : "Could not inspect PK3.");
    }
  }

  async function onConvert(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!file || !session || busy || !rights ||
        !validPk3RoomName(roomName) || !level) return;
    const signal = new AbortController();
    controller.current = signal;
    setJob(null); setMessage(""); setPublishedId(null); setReviewed(false);
    setPhase("starting");
    try {
      const result = await submitPk3Conversion({
        file, roomName, levelName: level, rightsConfirmed: rights,
        client, signal: signal.signal,
        progress: value => { setProgress(value); setPhase(value.phase); },
      });
      if (!signal.signal.aborted) {
        setJob(result); setPhase("review");
        setMessage(result.warnings.length
          ? "Conversion succeeded with warnings. Review them before publishing."
          : "Portable Project Prime map verified. Review and publish when ready.");
      }
    } catch (error) {
      if (signal.signal.aborted) {
        setPhase("paused");
        setMessage("Polling or upload paused. Re-select the same source and resume conversion.");
      } else {
        setPhase("error");
        setMessage(error instanceof Error ? error.message : "Source conversion failed.");
      }
    }
  }

  async function onPublish() {
    if (!job || phase !== "review" || !session ||
        (job.warnings.length > 0 && !reviewed)) return;
    const signal = new AbortController();
    controller.current = signal;
    setPhase("retrieving"); setMessage("");
    try {
      const map = await retrieveConvertedMap({
        job, client, signal: signal.signal,
        progress: value => { setProgress(value); setPhase(value.phase); },
      });
      const published = await publishMapPackage({
        map, client, visibility, notes,
        signal: signal.signal,
        progress: value => { setProgress(value); setPhase(value.phase); },
      });
      if (!signal.signal.aborted) {
        setPublishedId(published.mapId);
        setMessage("Converted map published through the existing Community catalog.");
        setPhase("complete");
      }
    } catch (error) {
      if (signal.signal.aborted) {
        setPhase("review");
        setMessage("Publication paused. Your converted package is retained for retry.");
      } else {
        setPhase("review");
        setMessage(error instanceof Error ? error.message : "Map publication failed.");
      }
    }
  }

  if (loading) return <section id={embedded ? "pk3-upload" : undefined}
    className="panel maps-upload pk3-upload">Checking Hunter License…</section>;
  if (!session) return embedded ? (
    <section id="pk3-upload" className="panel maps-upload pk3-upload">
      <span className="mini-tag">QUAKE SOURCE // AUTOMATED CONVERSION</span>
      <h2>Convert .pk3 to Project Prime.</h2>
      <p>Sign in with the Hunter License form above to convert and publish
        Quake maps without leaving this page.</p>
    </section>
  ) : <MapAccountPanel />;

  return (
    <div id={embedded ? "pk3-upload" : undefined} className="panel maps-upload pk3-upload">
      <span className="mini-tag">QUAKE SOURCE // AUTOMATED CONVERSION</span>
      <h2>Convert .pk3 to Project Prime.</h2>
      <p>Upload a Quake III .pk3 containing a compiled BSP. An isolated
        converter translates the level into a portable .ppmap.
        You review its warnings before publishing to the game.</p>

      {!online && <div className="maps-error" role="status">
        Website conversion is not enabled on this deployment yet. You can
        still import .pk3 locally in Project Prime Studio and publish .ppmap.
      </div>}

      <form onSubmit={onConvert}>
        <label htmlFor="pk3-source">Quake archive (.pk3, up to 128 MiB)</label>
        <input id="pk3-source" type="file" accept=".pk3" disabled={busy || !online}
          onChange={onChoose} required={!file} />
        {file && <>
          <label htmlFor="pk3-level">Compiled BSP level</label>
          <select id="pk3-level" value={level} disabled={busy}
            onChange={event => setLevel(event.target.value)}>
            {levels.map(name => <option key={name} value={name}>{name}</option>)}
          </select>
          <label htmlFor="pk3-room">Project Prime map name</label>
          <input id="pk3-room" type="text" required maxLength={40}
            value={roomName} disabled={busy}
            onChange={event => setRoomName(event.target.value)} />
          {roomName && !validPk3RoomName(roomName) &&
            <p className="maps-error">Use 1–40 letters, numbers, spaces, _ or -.
              Names must start with a letter/number and cannot contain paths.</p>}
          <label className="pk3-legal">
            <input type="checkbox" checked={rights} disabled={busy}
              onChange={event => setRights(event.target.checked)} />
            <span>I have permission to convert and redistribute this map, textures and
              included assets. I understand commercial game archives cannot be published
              without their owner's permission.</span>
          </label>
        </>}
        <div className="hero-actions">
          <button type="submit" className="button"
            disabled={!online || !file || !rights || !level ||
              !validPk3RoomName(roomName) || busy || phase === "review" || phase === "complete"}>
            {phase === "paused" || phase === "error"
              ? "Resume / Retry Conversion" : "Convert PK3"}
          </button>
          {busy && <button className="button button-ghost" type="button"
            onClick={() => controller.current?.abort()}>Pause</button>}
          <Link className="button button-ghost"
            href={embedded ? "#ppmap-upload" : "/maps/upload"}>
            {embedded ? "Back to .ppmap upload ↑" : "Upload .ppmap Instead"}
          </Link>
        </div>
      </form>

      {progress && <div className="maps-transfer" role="status">
        <strong>{progress.message}</strong>
        {phase !== "converting" && <progress value={percent} max={100} />}
        <span>{phase === "converting" ? "Converting…" : percent + "%"}</span>
      </div>}

      {job && <section className="pk3-job-review">
        <span className="mini-tag">CONVERSION VERIFIED</span>
        <h3>{job.roomName}</h3>
        <p>Map ID: <code>{job.mapId}</code></p>
        <p>Immutable package SHA-256: <code>{job.hash}</code></p>
        {job.warnings.length > 0 &&
          <div className="pk3-warning">
            <h4>Review conversion warnings</h4>
            <ul>{job.warnings.map((warning, index) =>
              <li key={index}>{warning}</li>)}</ul>
            <label className="pk3-legal">
              <input type="checkbox" checked={reviewed}
                onChange={event => setReviewed(event.target.checked)} />
              <span>I have reviewed these warnings and will test the converted
                map's textures, collision and gameplay.</span>
            </label>
          </div>}
        <label htmlFor="pk3-visibility">Publish visibility</label>
        <select id="pk3-visibility" value={visibility} disabled={busy}
          onChange={event => setVisibility(event.target.value as Visibility)}>
          <option value="Published">Public Community map</option>
          <option value="Unlisted">Unlisted link-only map</option>
          <option value="Draft">Private draft for testing</option>
        </select>
        <label htmlFor="pk3-notes">Release notes</label>
        <textarea id="pk3-notes" rows={3} maxLength={4000} value={notes}
          onChange={event => setNotes(event.target.value)}
          placeholder="Imported from Quake III, textures and gameplay reviewed…" />
        <div className="hero-actions">
          <button className="button" type="button" disabled={busy || phase === "complete" ||
            (job.warnings.length > 0 && !reviewed)}
            onClick={() => void onPublish()}>
            Publish Converted .ppmap
          </button>
          <Link href="/maps/my" className="button button-ghost">My Maps</Link>
        </div>
      </section>}

      {message && <p className={phase === "error" ? "maps-error" : "maps-success"}
        role="status">{message}</p>}
      {publishedId && <Link className="text-link"
        href={"/maps/" + publishedId}>View published map →</Link>}

      <p className="helper">
        Source uploads and conversion jobs remain private to your Hunter License.
        Re-select the same .pk3 and use Resume if your connection drops.
        Successful conversion does not guarantee competitive map balance or
        visual parity; test the map in Project Prime before public distribution.
      </p>
    </div>
  );
}
