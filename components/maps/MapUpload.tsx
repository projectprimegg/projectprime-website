"use client";

import Link from "next/link";
import { useEffect, useRef, useState, type ChangeEvent, type FormEvent } from "react";
import { useMapAuth } from "@/components/maps/MapAccount";
import { ensureMapUploadIdentity } from "@/lib/mapGuestUpload";
import {
  inspectMapPackage, publishMapPackage, type InspectedMap,
  type Visibility, type TransferProgress,
} from "@/lib/mapPublishing";
import {
  MAX_PK3_SOURCE_BYTES, retrieveConvertedMap, scanPk3Levels,
  submitPk3Conversion, validPk3RoomName, type ConvertedPk3Job,
} from "@/lib/pk3Publishing";
import { getMapUploadKind, type MapUploadKind } from "@/lib/mapUploadFormats";

const ACTIVE_PHASES = ["inspecting", "scanning", "authenticating", "hashing", "starting",
  "uploading", "converting", "retrieving", "publishing"];

function suggestedRoom(file: File): string {
  const name = file.name.replace(/\.pk3$/i, "").toUpperCase()
    .replace(/[^A-Z0-9 _-]/g, "_").replace(/^[_ -]+/, "");
  return "Q3_" + (name || "CUSTOM").slice(0, 36);
}

export function MapUpload() {
  const { client, session, loading } = useMapAuth();
  const controller = useRef<AbortController | null>(null);
  const [source, setSource] = useState<File | null>(null);
  const [kind, setKind] = useState<MapUploadKind | null>(null);
  const [map, setMap] = useState<InspectedMap | null>(null);
  const [levels, setLevels] = useState<string[]>([]);
  const [level, setLevel] = useState("");
  const [roomName, setRoomName] = useState("");
  const [rightsConfirmed, setRightsConfirmed] = useState(false);
  const [job, setJob] = useState<ConvertedPk3Job | null>(null);
  const [warningsReviewed, setWarningsReviewed] = useState(false);
  const [phase, setPhase] = useState("idle");
  const [notice, setNotice] = useState("");
  const [noticeIsError, setNoticeIsError] = useState(false);
  const [progress, setProgress] = useState<TransferProgress | null>(null);
  const [visibility, setVisibility] = useState<Visibility>("Published");
  const [notes, setNotes] = useState("");
  const [publishedId, setPublishedId] = useState<string | null>(null);

  const converterConfigured = Boolean(process.env.NEXT_PUBLIC_PK3_CONVERSION_URL);
  const busy = ACTIVE_PHASES.includes(phase);
  const canConvertPk3 = kind === "pk3" && source !== null && levels.length > 0 &&
    Boolean(level) && validPk3RoomName(roomName) && rightsConfirmed && converterConfigured;
  const canPublishPk3 = kind === "pk3" && job !== null &&
    (job.warnings.length === 0 || warningsReviewed);
  const canSubmit = kind === "ppmap" ? map !== null :
    job ? canPublishPk3 : canConvertPk3;
  const percent = progress?.total
    ? Math.max(0, Math.min(100, Math.floor(progress.done * 100 / progress.total))) : 0;

  useEffect(() => () => controller.current?.abort(), []);

  async function selectFile(event: ChangeEvent<HTMLInputElement>) {
    const selected = event.currentTarget.files?.[0];
    if (!selected || busy) return;
    // Allow the same file to be chosen again after a failed preflight.
    event.currentTarget.value = "";
    controller.current?.abort();
    const operation = new AbortController();
    controller.current = operation;
    setSource(null); setKind(null); setMap(null); setJob(null);
    setLevels([]); setLevel(""); setRoomName("");
    setRightsConfirmed(false); setWarningsReviewed(false);
    setPublishedId(null); setProgress(null); setNotice("");
    setNoticeIsError(false); setVisibility("Published"); setNotes("");

    const detected = getMapUploadKind(selected.name);
    if (!detected) {
      setPhase("error");
      setNoticeIsError(true);
      setNotice("Choose a Project Prime .ppmap or Quake .pk3 archive.");
      return;
    }
    setSource(selected);
    setKind(detected);
    try {
      if (detected === "ppmap") {
        setPhase("inspecting");
        const inspected = await inspectMapPackage(selected, operation.signal, update => {
          if (!operation.signal.aborted) { setProgress(update); setPhase(update.phase); }
        });
        if (operation.signal.aborted) return;
        setMap(inspected);
      } else {
        setPhase("scanning");
        if (!selected.size || selected.size > MAX_PK3_SOURCE_BYTES)
          throw new Error("Quake .pk3 archives must be between 1 byte and 128 MiB.");
        const found = await scanPk3Levels(selected);
        if (operation.signal.aborted) return;
        setLevels(found);
        setLevel(found[0]);
        setRoomName(suggestedRoom(selected));
      }
      setPhase("ready");
    } catch (error) {
      if (operation.signal.aborted) {
        setPhase("paused");
        setNotice("Inspection paused. Choose the same file to restart.");
      } else {
        setPhase("error");
        setNoticeIsError(true);
        setNotice(error instanceof Error ? error.message : "Unable to inspect this map.");
      }
    }
  }

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (busy || !canSubmit || phase === "complete") return;

    const operation = new AbortController();
    controller.current = operation;
    setNotice(""); setNoticeIsError(false); setPublishedId(null); setProgress(null);
    const showProgress = (update: TransferProgress) => {
      if (!operation.signal.aborted) { setProgress(update); setPhase(update.phase); }
    };
    try {
      if (!session) {
        setPhase("authenticating");
        await ensureMapUploadIdentity(client, session);
        if (operation.signal.aborted) return;
      }
      if (kind === "ppmap" && map) {
        setPhase("starting");
        const published = await publishMapPackage({
          map, client, visibility, notes, signal: operation.signal,
          progress: showProgress,
        });
        if (!operation.signal.aborted) {
          setPhase("complete");
          setPublishedId(published.mapId);
          setNotice("Your map is published in the Community catalog.");
        }
      } else if (kind === "pk3" && source) {
        if (!job) {
          setPhase("starting");
          const converted = await submitPk3Conversion({
            file: source, roomName, levelName: level,
            rightsConfirmed, client, signal: operation.signal,
            progress: showProgress,
          });
          if (!operation.signal.aborted) {
            setJob(converted);
            setPhase("review");
            setNotice(converted.warnings.length
              ? "Conversion succeeded with warnings. Review them before publishing."
              : "Conversion verified. Review and publish when ready.");
          }
        } else {
          setPhase("retrieving");
          const convertedMap = await retrieveConvertedMap({
            job, client, signal: operation.signal, progress: showProgress,
          });
          if (operation.signal.aborted) return;
          setPhase("publishing");
          const published = await publishMapPackage({
            map: convertedMap, client, visibility, notes,
            signal: operation.signal, progress: showProgress,
          });
          if (!operation.signal.aborted) {
            setPhase("complete");
            setPublishedId(published.mapId);
            setNotice("Converted map published through the Community catalog.");
          }
        }
      }
    } catch (error) {
      if (operation.signal.aborted) {
        setPhase(job ? "review" : "paused");
        setNotice(job
          ? "Publication paused. The converted package is retained for retry."
          : "Transfer paused. Use the button again to resume.");
      } else {
        // A converted PK3 remains available for another publishing attempt.
        setPhase(kind === "pk3" && job ? "review" : "error");
        setNoticeIsError(true);
        setNotice(error instanceof Error ? error.message : "Map upload failed.");
      }
    }
  }

  if (loading) return <section className="panel maps-upload"><p>Checking Hunter License…</p></section>;

  return (
    <form id="map-upload" className="panel maps-upload" onSubmit={submit}>
      <span className="mini-tag">WEBSITE TO GAME // CREATOR PIPELINE</span>
      <h2>Upload a playable map.</h2>
      <p>Select a Project Prime package or a Quake arena. The format is detected
        automatically and the appropriate publishing workflow is used.</p>
      {!session && <div className="maps-upload-guest" role="note">
        <strong>No registration required.</strong>
        <p>Choose your map below, then publish or convert as a guest. We'll create
          a temporary browser-bound identity only when you submit, so your uploads
          retain private ownership and the usual validation checks.</p>
        <p>Guest ownership can be lost if you sign out or clear this browser's
          data. <Link href="/maps/account">Sign in with a Hunter License</Link> first
          if you want to manage your maps across devices.</p>
      </div>}
      {session?.user.is_anonymous && <p className="helper">
        Publishing as a guest. Keep this browser's session to manage your maps;
        signing out or clearing browser data may permanently lose access.
      </p>}

      <label htmlFor="map-package">Map file (.ppmap up to 512 MiB or .pk3 up to 128 MiB)</label>
      <input id="map-package" type="file" accept=".ppmap,.pk3"
        disabled={busy} onChange={selectFile} required={!source} />

      {source && kind && <div className="maps-upload-inspection" aria-live="polite">
        <span className="mini-tag">{kind === "pk3" ? "QUAKE PK3 // CONVERSION" : "PROJECT PRIME // PUBLISH"}</span>
        <h3>{source.name}</h3>
        <p>Detected format: <strong>{kind === "pk3" ? "Quake .pk3" : "Project Prime .ppmap"}</strong></p>
        {kind === "ppmap" && map && <>
          <p>Runtime name: {map.manifest.name}</p>
          <p>Map ID: {map.manifest.mapId}</p>
          <p>Version: {map.manifest.mapVersion || "Unspecified"}</p>
          <p>SHA-256: <code>{map.hash}</code></p>
        </>}
      </div>}

      {kind === "pk3" && <>
        {!converterConfigured && <p className="maps-error" role="status">
          PK3 conversion is not enabled on this deployment. The separate
          Linux conversion service and NEXT_PUBLIC_PK3_CONVERSION_URL must be configured.
          You can still import the archive in Project Prime Studio and publish its .ppmap.
        </p>}

        {levels.length > 0 && !job && <>
          <label htmlFor="pk3-level">Compiled BSP level</label>
          <select id="pk3-level" value={level} disabled={busy}
            onChange={event => setLevel(event.target.value)}>
            {levels.map(name => <option value={name} key={name}>{name}</option>)}
          </select>

          <label htmlFor="pk3-room">Project Prime map name</label>
          <input id="pk3-room" type="text" required maxLength={40}
            value={roomName} disabled={busy}
            onChange={event => setRoomName(event.target.value)} />
          {roomName && !validPk3RoomName(roomName) &&
            <p className="maps-error">Use 1–40 letters, numbers, spaces, _ or -.
              Start with a letter or number; paths are not allowed.</p>}

          <label className="pk3-legal">
            <input type="checkbox" checked={rightsConfirmed} disabled={busy}
              onChange={event => setRightsConfirmed(event.target.checked)} />
            <span>I have permission to convert and redistribute this map, textures
              and included assets, including any commercial content.</span>
          </label>
        </>}

        {job && <section className="pk3-job-review">
          <span className="mini-tag">CONVERSION VERIFIED</span>
          <h3>{job.roomName}</h3>
          <p>Map ID: <code>{job.mapId}</code></p>
          <p>Verified package SHA-256: <code>{job.hash}</code></p>
          {job.warnings.length > 0 && <div className="pk3-warning">
            <h4>Review conversion warnings before publishing</h4>
            <ul>{job.warnings.map((warning, index) =>
              <li key={index}>{warning}</li>)}</ul>
            <label className="pk3-legal">
              <input type="checkbox" checked={warningsReviewed} disabled={busy}
                onChange={event => setWarningsReviewed(event.target.checked)} />
              <span>I have reviewed the warnings and will test textures, collision,
                spawns and gameplay before sharing this map.</span>
            </label>
          </div>}
        </section>}
      </>}

      {((kind === "ppmap" && map) || (kind === "pk3" && job)) && <>
        <label htmlFor="map-visibility">Publish visibility</label>
        <select id="map-visibility" value={visibility} disabled={busy}
          onChange={event => setVisibility(event.target.value as Visibility)}>
          <option value="Published">Published: searchable in-game and on the website</option>
          <option value="Unlisted">Unlisted: shareable by exact link</option>
          <option value="Draft">Draft: private to your Hunter License</option>
        </select>

        <label htmlFor="map-notes">Release notes (optional)</label>
        <textarea id="map-notes" maxLength={4000} rows={4} value={notes}
          disabled={busy} onChange={event => setNotes(event.target.value)}
          placeholder="What changed in this map?" />
      </>}

      {progress && <div className="maps-transfer" role="status">
        <strong>{progress.message}</strong>
        {phase !== "converting" && <progress value={percent} max={100}
          aria-label="Map upload progress" />}
        <span>{phase === "converting" ? "Converting…" : percent + "%"}</span>
      </div>}

      {notice && <p role={noticeIsError ? "alert" : "status"}
        className={noticeIsError ? "maps-error" : "maps-success"}>{notice}</p>}

      <div className="hero-actions">
        <button type="submit" className="button"
          disabled={!canSubmit || busy || phase === "complete"}>
          {kind === "pk3"
            ? job ? "Publish Converted Map" : session ? "Convert PK3" : "Convert PK3 as Guest"
            : session ? "Publish Map" : "Publish as Guest"}
        </button>
        {busy && <button type="button" className="button button-ghost"
          onClick={() => controller.current?.abort()}>Pause Transfer</button>}
        <Link className="button button-ghost" href="/maps/my">My Maps</Link>
      </div>

      {publishedId && <Link className="text-link" href={"/maps/" + publishedId}>
        View published map →
      </Link>}
      <p className="helper">
        {kind === "pk3"
          ? "PK3 uploads convert in a private service. After conversion, review the result and publish the verified .ppmap to Community."
          : "Existing .ppmap packages publish directly. Interrupted uploads can resume using the same file and server-confirmed offset."}
      </p>
    </form>
  );
}
