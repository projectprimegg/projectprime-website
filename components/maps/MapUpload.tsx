"use client";

import Link from "next/link";
import { useEffect, useRef, useState, type ChangeEvent, type FormEvent } from "react";
import { MapAccountPanel, useMapAuth } from "@/components/maps/MapAccount";
import {
  inspectMapPackage, publishMapPackage, type InspectedMap,
  type Visibility, type TransferProgress,
} from "@/lib/mapPublishing";

export function MapUpload() {
  const { client, session, loading } = useMapAuth();
  const controller = useRef<AbortController | null>(null);
  const [map, setMap] = useState<InspectedMap | null>(null);
  const [phase, setPhase] = useState("idle");
  const [notice, setNotice] = useState("");
  const [progress, setProgress] = useState<TransferProgress | null>(null);
  const [visibility, setVisibility] = useState<Visibility>("Published");
  const [notes, setNotes] = useState("");
  const [publishedId, setPublishedId] = useState<string | null>(null);

  useEffect(() => () => controller.current?.abort(), []);
  const active = ["inspecting", "hashing", "starting", "uploading", "publishing"]
    .includes(phase);

  async function selectFile(event: ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0];
    if (!file || active) return;
    controller.current?.abort();
    const abort = new AbortController();
    controller.current = abort;
    setMap(null); setPublishedId(null); setNotice("");
    setPhase("inspecting");
    try {
      const result = await inspectMapPackage(file, abort.signal, update => {
        setProgress(update); setPhase(update.phase);
      });
      if (!abort.signal.aborted) { setMap(result); setPhase("ready"); }
    } catch (error) {
      if (abort.signal.aborted) setPhase("paused");
      else { setPhase("error"); setNotice(error instanceof Error ? error.message :
        "Could not inspect this package."); }
    }
  }

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!map || !session || active) return;
    const abort = new AbortController();
    controller.current = abort;
    setNotice(""); setPublishedId(null); setPhase("starting");
    try {
      const result = await publishMapPackage({
        map, client, visibility, notes, signal: abort.signal,
        progress: update => { setProgress(update); setPhase(update.phase); },
      });
      if (!abort.signal.aborted) {
        setPhase("complete");
        setPublishedId(result.mapId);
        setNotice("Your map is published. It is now available to the Community catalog.");
      }
    } catch (error) {
      if (abort.signal.aborted) {
        setPhase("paused");
        setNotice("Transfer paused. Choose Publish again with the same file to resume.");
      } else {
        setPhase("error");
        setNotice(error instanceof Error ? error.message : "Map publication failed.");
      }
    }
  }

  if (loading) return <section className="panel maps-upload"><p>Checking Hunter License…</p></section>;
  if (!session) return <MapAccountPanel />;

  const percentage = progress?.total
    ? Math.max(0, Math.min(100, Math.floor(progress.done * 100 / progress.total))) : 0;

  return (
    <form id="ppmap-upload" className="panel maps-upload" onSubmit={submit}>
      <span className="mini-tag">WEBSITE TO GAME // CREATOR PIPELINE</span>
      <h2>Upload a playable map.</h2>
      <p>Export a .ppmap from Project Prime Studio. The server verifies its
        contents and publishes the exact immutable archive used by players.</p>

      <label htmlFor="map-package">Map package (.ppmap, maximum 512 MiB)</label>
      <input id="map-package" type="file" accept=".ppmap" disabled={active}
        onChange={selectFile} required={!map} />

      {map && <div className="maps-upload-inspection">
        <h3>{map.manifest.displayName || map.manifest.name}</h3>
        <p>Runtime name: {map.manifest.name}</p>
        <p>Map ID: {map.manifest.mapId}</p>
        <p>Version: {map.manifest.mapVersion || "Unspecified"}</p>
        <p>SHA-256: <code>{map.hash}</code></p>
      </div>}

      <label htmlFor="map-visibility">Publish visibility</label>
      <select id="map-visibility" value={visibility} disabled={active}
        onChange={event => setVisibility(event.target.value as Visibility)}>
        <option value="Published">Published: searchable in-game and on the website</option>
        <option value="Unlisted">Unlisted: hidden from discovery, shareable by exact link</option>
        <option value="Draft">Draft: private to your Hunter License</option>
      </select>

      <label htmlFor="map-notes">Release notes (optional)</label>
      <textarea id="map-notes" maxLength={4000} rows={4} value={notes}
        disabled={active} onChange={event => setNotes(event.target.value)}
        placeholder="What changed in this revision?" />

      {progress && <div className="maps-transfer">
        <strong>{progress.message}</strong>
        <progress value={percentage} max={100} aria-label="Map upload progress" />
        <span>{percentage}%</span>
      </div>}

      {notice && <p role={phase === "error" ? "alert" : "status"}
        className={phase === "error" ? "maps-error" : "maps-success"}>{notice}</p>}

      <div className="hero-actions">
        <button type="submit" className="button"
          disabled={!map || active || phase === "complete"}>
          {phase === "paused" || phase === "error" ? "Retry / Resume Upload" : "Publish Map"}
        </button>
        {active && <button type="button" className="button button-ghost"
          onClick={() => controller.current?.abort()}>Pause Transfer</button>}
        <Link className="button button-ghost" href="/maps/my">My Maps</Link>
      </div>
      {publishedId && <Link className="text-link" href={"/maps/" + publishedId}>
        View published map →
      </Link>}
      <p className="helper">If the connection drops, selecting the same file
        and retrying resumes at the server-confirmed offset. Publishing an
        existing Map ID checks its current revision and refuses stale updates.</p>
    </form>
  );
}
