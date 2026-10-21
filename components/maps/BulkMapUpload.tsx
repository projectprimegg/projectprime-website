"use client";

import Link from "next/link";
import { useEffect, useRef, useState, type ChangeEvent, type FormEvent } from "react";
import { useMapAuth } from "@/components/maps/MapAccount";
import { ensureMapUploadIdentity } from "@/lib/mapGuestUpload";
import {
  inspectMapPackage, publishMapPackage, type InspectedMap,
  type Visibility, type TransferProgress,
} from "@/lib/mapPublishing";
import { MapPublishSession } from "@/lib/mapPublishSession";
import {
  MAX_PK3_SOURCE_BYTES, retrieveConvertedMap, scanPk3Levels,
  submitPk3Conversion, validPk3RoomName, type ConvertedPk3Job,
} from "@/lib/pk3Publishing";
import {
  MAX_BULK_MAPS, planMapBatch, type MapUploadKind,
} from "@/lib/mapUploadFormats";
import { usePk3ServiceAvailability } from "@/lib/usePk3ServiceAvailability";

type UploadStatus = "queued" | "inspecting" | "ready" | "review" | "publishing" |
  "complete" | "error" | "paused";

type UploadItem = {
  id: number;
  key: string;
  source: File;
  kind: MapUploadKind;
  status: UploadStatus;
  map: InspectedMap | null;
  levels: string[];
  level: string;
  roomName: string;
  rightsConfirmed: boolean;
  warningsReviewed: boolean;
  job: ConvertedPk3Job | null;
  progress: TransferProgress | null;
  error: string;
  publishedId: string | null;
};

function suggestedRoom(file: File): string {
  const name = file.name.replace(/\.pk3$/i, "").toUpperCase()
    .replace(/[^A-Z0-9 _-]/g, "_").replace(/^[_ -]+/, "");
  return "Q3_" + (name || "CUSTOM").slice(0, 36);
}

function statusLabel(item: UploadItem): string {
  if (item.status === "complete") return "Published";
  if (item.status === "review") return "Converted: review before publishing";
  if (item.status === "error") return "Failed: retry available";
  if (item.status === "paused") return "Paused: resume available";
  if (item.status === "publishing") return item.progress?.message || "Processing";
  if (item.status === "inspecting") return "Inspecting";
  if (item.status === "queued") return "Queued";
  if (item.kind === "pk3" && !item.rightsConfirmed) return "Awaiting rights confirmation";
  if (item.kind === "pk3" && !item.job) return "Ready for conversion";
  return "Ready";
}

export function BulkMapUpload() {
  const { client, session, loading } = useMapAuth();
  // Keep a synchronous queue snapshot for sequential async work. React renders
  // can be deferred; never run another upload using a stale closure snapshot.
  const queueRef = useRef<UploadItem[]>([]);
  const nextId = useRef(0);
  const controller = useRef<AbortController | null>(null);
  const stopRequested = useRef(false);
  const running = useRef(false);
  const [queue, setQueue] = useState<UploadItem[]>([]);
  const [preparing, setPreparing] = useState(false);
  const [processing, setProcessing] = useState(false);
  const [notice, setNotice] = useState("");
  const [visibility, setVisibility] = useState<Visibility>("Published");
  const [notes, setNotes] = useState("");
  const converter = usePk3ServiceAvailability();
  const converterAvailable = converter.status === "available";
  const busy = preparing || processing;
  const completed = queue.filter(item => item.status === "complete").length;
  const remaining = queue.length - completed;
  const hasReadyWork = queue.some(item => item.status !== "complete" &&
    (item.kind === "ppmap" || item.job !== null || item.rightsConfirmed));

  useEffect(() => () => { stopRequested.current = true; controller.current?.abort(); }, []);

  function replaceQueue(items: UploadItem[]) {
    queueRef.current = items;
    setQueue(items);
  }
  function patchItem(id: number, change: Partial<UploadItem>) {
    replaceQueue(queueRef.current.map(item => item.id === id ? { ...item, ...change } : item));
  }
  function currentItem(id: number): UploadItem | undefined {
    return queueRef.current.find(item => item.id === id);
  }
  function showProgress(id: number, signal: AbortSignal, progress: TransferProgress) {
    if (!signal.aborted) patchItem(id, { status: "publishing", progress });
  }

  async function inspectItem(id: number, signal: AbortSignal): Promise<boolean> {
    const item = currentItem(id);
    if (!item || signal.aborted) return false;
    patchItem(id, { status: "inspecting", error: "", progress: null });
    try {
      if (item.kind === "ppmap") {
        const map = await inspectMapPackage(item.source, signal,
          progress => showProgress(id, signal, progress));
        if (signal.aborted) throw new DOMException("Inspection paused.", "AbortError");
        patchItem(id, { map, status: "ready", progress: null });
      } else {
        if (item.source.size > MAX_PK3_SOURCE_BYTES)
          throw new Error("PK3 exceeds the 128 MiB source limit.");
        const levels = await scanPk3Levels(item.source);
        if (signal.aborted) throw new DOMException("Inspection paused.", "AbortError");
        patchItem(id, { levels, level: item.level && levels.includes(item.level) ? item.level : levels[0],
          roomName: item.roomName || suggestedRoom(item.source), status: "ready", progress: null });
      }
      return true;
    } catch (error) {
      patchItem(id, { status: signal.aborted ? "paused" : "error",
        error: signal.aborted ? "Inspection paused. Resume to retry." :
          error instanceof Error ? error.message : "Map inspection failed." });
      return false;
    }
  }

  async function selectFiles(event: ChangeEvent<HTMLInputElement>) {
    const chosen = Array.from(event.currentTarget.files || []);
    // The same files may be selected again after removal.
    event.currentTarget.value = "";
    if (busy || chosen.length === 0) return;
    const selection = planMapBatch(chosen, queueRef.current.map(item => item.source));
    if (selection.rejected.length)
      setNotice(selection.rejected.map(entry => entry.file.name + ": " + entry.reason).join("\n"));
    else setNotice("");
    if (!selection.accepted.length) return;
    const incoming: UploadItem[] = selection.accepted.map(({ file, kind, key }) => ({
      id: nextId.current++, key, source: file, kind, status: "queued",
      map: null, levels: [], level: "", roomName: suggestedRoom(file),
      rightsConfirmed: false, warningsReviewed: false, job: null,
      progress: null, error: "", publishedId: null,
    }));
    replaceQueue([...queueRef.current, ...incoming]);
    const operation = new AbortController();
    controller.current = operation;
    stopRequested.current = false;
    setPreparing(true);
    try {
      for (const item of incoming) {
        if (operation.signal.aborted || stopRequested.current) break;
        await inspectItem(item.id, operation.signal);
      }
    } finally {
      for (const item of incoming) {
        const latest = currentItem(item.id);
        if (latest?.status === "queued")
          patchItem(item.id, { status: "paused", error: "Inspection paused. Resume to retry." });
      }
      if (controller.current === operation) controller.current = null;
      setPreparing(false);
    }
  }

  async function submitBatch(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (busy || running.current || !hasReadyWork) return;
    running.current = true;
    stopRequested.current = false;
    const operation = new AbortController();
    const publishSession = new MapPublishSession();
    const publishInspectedMap = async (
      map: InspectedMap,
      progress: (value: TransferProgress) => void,
    ) => {
      try {
        return await publishMapPackage({
          map, client, visibility, notes, signal: operation.signal,
          progress, publishSession,
        });
      } catch (error) {
        publishSession.invalidate();
        throw error;
      }
    };
    controller.current = operation;
    setProcessing(true);
    setNotice("");
    let authorized = Boolean(session);
    let finished = 0;
    let converted = 0;
    let failed = 0;
    try {
      // Sequential by design: bounded browser memory, independent server
      // reservations, and no parallel conversion jobs hammering the map host.
      for (const original of [...queueRef.current]) {
        if (stopRequested.current || operation.signal.aborted) break;
        let item = currentItem(original.id);
        if (!item || item.status === "complete") continue;
        if (!item.map && !item.job && item.kind === "ppmap" ||
            !item.job && item.kind === "pk3" && !item.levels.length) {
          if (!await inspectItem(item.id, operation.signal)) { failed++; continue; }
          item = currentItem(original.id);
          if (!item) continue;
        }
        if (item.kind === "pk3") {
          // Never silently publish Quake assets before a creator has
          // acknowledged the redistribution rights and conversion warnings.
          if (!item.rightsConfirmed) continue;
          if (!item.job && !converterAvailable) continue;
          if (!item.job && (!item.level || !validPk3RoomName(item.roomName))) continue;
          if (item.job && item.job.warnings.length && !item.warningsReviewed) continue;
        }
        try {
          if (!authorized) {
            await ensureMapUploadIdentity(client, session);
            authorized = true;
          }
          if (operation.signal.aborted) break;
          const progress = (value: TransferProgress) =>
            showProgress(original.id, operation.signal, value);
          patchItem(original.id, { status: "publishing", error: "", progress: null });
          if (item.kind === "ppmap" && item.map) {
            const result = await publishInspectedMap(item.map, progress);
            if (operation.signal.aborted) break;
            patchItem(original.id, { status: "complete", publishedId: result.mapId, progress: null });
            finished++;
          } else if (item.kind === "pk3" && !item.job) {
            const job = await submitPk3Conversion({
              file: item.source, roomName: item.roomName, levelName: item.level,
              rightsConfirmed: item.rightsConfirmed, client,
              signal: operation.signal, progress,
            });
            if (operation.signal.aborted) break;
            patchItem(original.id, {
              job, status: "review", progress: null, warningsReviewed: false,
            });
            converted++;
          } else if (item.kind === "pk3" && item.job) {
            const map = await retrieveConvertedMap({
              job: item.job, client, signal: operation.signal, progress,
            });
            if (operation.signal.aborted) break;
            const result = await publishInspectedMap(map, progress);
            if (operation.signal.aborted) break;
            patchItem(original.id, { status: "complete", publishedId: result.mapId, progress: null });
            finished++;
          }
        } catch (error) {
          if (operation.signal.aborted) {
            patchItem(original.id, { status: "paused", error: "Transfer paused. Resume the batch." });
            break;
          }
          failed++;
          patchItem(original.id, { status: "error", error: error instanceof Error ?
            error.message : "Map transfer failed. Retry this file." });
          // A single invalid or conflicting map must not stop the other maps.
        }
      }
      if (stopRequested.current || operation.signal.aborted) {
        setNotice("Batch paused. Completed maps are retained; retry resumes unfinished maps.");
      } else {
        const action = converted ? " " + converted + " converted maps require review and a second Publish pass." : "";
        setNotice(finished + " maps published, " + failed + " failed." + action +
          " Unconfirmed PK3 maps and conversion warnings remain in the queue.");
      }
    } finally {
      if (controller.current === operation) controller.current = null;
      running.current = false;
      setProcessing(false);
    }
  }

  function pause() {
    stopRequested.current = true;
    controller.current?.abort();
  }
  function remove(id: number) {
    if (!busy) replaceQueue(queueRef.current.filter(item => item.id !== id));
  }

  if (loading) return <section className="panel maps-upload">Checking Hunter License…</section>;
  return (
    <form id="map-upload" className="panel maps-upload" onSubmit={submitBatch}>
      <span className="mini-tag">WEBSITE TO GAME // BULK CREATOR PIPELINE</span>
      <h2>Upload playable maps in bulk.</h2>
      <p>Select up to {MAX_BULK_MAPS} mixed .ppmap and .pk3 archives.
        Each map is inspected and published separately. Uploads run one at a time
        to protect browser memory and the Community server.</p>
      {!session && <div className="maps-upload-guest" role="note">
        <strong>No registration required.</strong>
        <p>Choose files first. A temporary Supabase guest identity is created
          only when you publish or convert. Keep the browser session to retain ownership,
          or <Link href="/maps/account">sign in</Link> for access across devices.</p>
      </div>}
      {session?.user.is_anonymous && <p className="helper">
        Guest ownership is tied to this browser session and can be lost if you sign out
        or clear site data.
      </p>}

      <label htmlFor="map-package">Map archives (.ppmap up to 512 MiB, .pk3 up to 128 MiB each)</label>
      <input id="map-package" type="file" multiple accept=".ppmap,.pk3"
        disabled={busy || queue.length >= MAX_BULK_MAPS} onChange={selectFiles} />
      <p className="helper">Add files in several selections. Duplicate selections are skipped.
        Remove individual files to free queue space.</p>

      {queue.length > 0 && <>
        <div className="maps-bulk-summary" role="status">
          <strong>{completed} of {queue.length} published</strong>
          <span>{remaining} remaining or awaiting review</span>
          {!busy && <button className="button button-ghost" type="button"
            onClick={() => replaceQueue(queueRef.current.filter(item => item.status !== "complete"))}>
            Clear completed
          </button>}
        </div>
        <div className="maps-bulk-list">
          {queue.map(item => {
            const percent = item.progress?.total
              ? Math.min(100, Math.max(0, Math.floor(item.progress.done * 100 / item.progress.total))) : 0;
            return <section className="maps-bulk-item" key={item.id}>
              <div className="maps-bulk-heading">
                <div>
                  <strong>{item.source.name}</strong>
                  <small>{item.kind.toUpperCase()} · {(item.source.size / 1048576).toFixed(1)} MiB</small>
                </div>
                <span>{statusLabel(item)}</span>
              </div>
              {item.kind === "ppmap" && item.map && <p className="helper">
                {item.map.manifest.name} · Map ID: <code>{item.map.manifest.mapId}</code>
              </p>}
              {item.kind === "pk3" && !item.job && item.levels.length > 0 && <>
                <label htmlFor={"map-level-" + item.id}>BSP level</label>
                <select id={"map-level-" + item.id} value={item.level} disabled={busy}
                  onChange={event => patchItem(item.id, { level: event.target.value })}>
                  {item.levels.map(level => <option value={level} key={level}>{level}</option>)}
                </select>
                <label htmlFor={"map-room-" + item.id}>Project Prime map name</label>
                <input id={"map-room-" + item.id} type="text" maxLength={40}
                  value={item.roomName} disabled={busy}
                  onChange={event => patchItem(item.id, { roomName: event.target.value })} />
                {!validPk3RoomName(item.roomName) && <p className="maps-error">
                  Enter a valid map name (1–40 letters, numbers, spaces, _ or -).
                </p>}
                <label className="pk3-legal">
                  <input type="checkbox" checked={item.rightsConfirmed} disabled={busy}
                    onChange={value => patchItem(item.id, { rightsConfirmed: value.target.checked })} />
                  <span>I have permission to convert and redistribute this map
                    and its textures and included assets.</span>
                </label>
              </>}
              {item.kind === "pk3" && !converterAvailable && !item.job && <div className="maps-error">
                {converter.status === "checking" ? "Checking PK3 converter…" :
                  "PK3 conversion is unavailable. .ppmap files can still be published."}
              </div>}
              {item.job && <div className="pk3-job-review">
                <strong>Converted: {item.job.roomName}</strong>
                <p>Verified SHA-256: <code>{item.job.hash}</code></p>
                {item.job.warnings.length > 0 && <div className="pk3-warning">
                  <h4>Review conversion warnings</h4>
                  <ul>{item.job.warnings.map((warning, index) =>
                    <li key={index}>{warning}</li>)}</ul>
                  <label className="pk3-legal">
                    <input type="checkbox" checked={item.warningsReviewed} disabled={busy}
                      onChange={event => patchItem(item.id, { warningsReviewed: event.target.checked })} />
                    <span>I reviewed the warnings and will test the converted collision,
                      textures, spawns and gameplay before sharing.</span>
                  </label>
                </div>}
                {item.status !== "complete" && <p className="helper">
                  Conversion complete. Select Publish remaining maps to publish this result.
                </p>}
              </div>}
              {item.progress && <div className="maps-transfer" role="status">
                <strong>{item.progress.message}</strong>
                {item.progress.phase !== "converting" && <progress value={percent} max={100}
                  aria-label={item.source.name + " progress"} />}
                <span>{item.progress.phase === "converting" ? "Converting…" : percent + "%"}</span>
              </div>}
              {item.error && <p className="maps-error" role="alert">{item.error}</p>}
              {item.publishedId && <Link className="text-link"
                href={"/maps/" + item.publishedId}>View published map →</Link>}
              {!busy && <button type="button" className="button button-ghost"
                onClick={() => remove(item.id)}>Remove</button>}
            </section>;
          })}
        </div>
      </>}

      {queue.length > 0 && <>
        <label htmlFor="batch-visibility">Visibility for maps in this batch</label>
        <select id="batch-visibility" value={visibility} disabled={busy}
          onChange={event => setVisibility(event.target.value as Visibility)}>
          <option value="Published">Public Community catalog</option>
          <option value="Unlisted">Unlisted (share by exact link)</option>
          <option value="Draft">Private draft</option>
        </select>
        <label htmlFor="batch-notes">Shared release notes (optional)</label>
        <textarea id="batch-notes" maxLength={4000} rows={3} value={notes}
          disabled={busy} onChange={event => setNotes(event.target.value)}
          placeholder="Applies to each publication in this batch…" />
      </>}
      {notice && <p className={notice.includes("failed") ? "maps-error" : "maps-success"}
        role="status" style={{ whiteSpace: "pre-line" }}>{notice}</p>}
      <div className="hero-actions">
        <button className="button" type="submit" disabled={busy || !hasReadyWork}>
          {processing ? "Uploading…" : session ? "Publish remaining maps" : "Publish / Convert as Guest"}
        </button>
        {busy && <button type="button" className="button button-ghost" onClick={pause}>
          Pause batch
        </button>}
        {converter.status === "unavailable" && queue.some(item => item.kind === "pk3") &&
          <button type="button" className="button button-ghost" onClick={converter.retry}>
            Check converter again
          </button>}
        <Link className="button button-ghost" href="/maps/my">My Maps</Link>
      </div>
      <p className="helper">Interrupted .ppmap transfers resume using the service-confirmed byte offset.
        PK3 conversion requires a separate review pass; warnings are never silently approved.
        A failed map does not prevent later maps in the batch from uploading.</p>
    </form>
  );
}
