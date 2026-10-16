"use client";

import Link from "next/link";
import { useCallback, useEffect, useState } from "react";
import { MapAccountPanel, useMapAuth } from "@/components/maps/MapAccount";
import { communityApiUrl, isMapHash, type MapProject, type MapRevision } from "@/lib/communityMaps";
import { mintCommunityTicket, ownedMapProjects, type Visibility } from "@/lib/mapPublishing";

export function MapCreatorDashboard() {
  const { client, session, loading: authLoading } = useMapAuth();
  const [projects, setProjects] = useState<MapProject[]>([]);
  const [revisions, setRevisions] = useState<Record<string, MapRevision[]>>({});
  const [expanded, setExpanded] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [working, setWorking] = useState(false);
  const [error, setError] = useState("");
  const [note, setNote] = useState("");

  const load = useCallback(async () => {
    if (!session) return;
    setLoading(true); setError("");
    try {
      const ticket = await mintCommunityTicket(client);
      const result = await ownedMapProjects(ticket);
      setProjects(result.sort((a, b) => (b.updatedAt || "").localeCompare(a.updatedAt || "")));
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : "Unable to load your maps.");
    } finally { setLoading(false); }
  }, [session, client]);

  useEffect(() => { void load(); }, [load]);

  async function send(method: string, path: string, value?: string) {
    const ticket = await mintCommunityTicket(client);
    const response = await fetch(communityApiUrl(path), {
      method, cache: "no-store",
      headers: {
        Authorization: "Bearer " + ticket,
        ...(value !== undefined ? { "Content-Type": "application/json" } : {}),
      },
      body: value === undefined ? undefined : JSON.stringify(value),
    });
    if (!response.ok) {
      const payload = await response.json().catch(() => ({})) as { message?: string };
      throw new Error(payload.message || "Community operation failed (HTTP " + response.status + ").");
    }
    return response;
  }

  async function showRevisions(project: MapProject) {
    if (expanded === project.mapId) { setExpanded(null); return; }
    setExpanded(project.mapId); setError("");
    try {
      const response = await send("GET", "v2/maps/" + project.mapId + "/revisions");
      const data: unknown = await response.json();
      if (!Array.isArray(data)) throw new Error("Invalid revision history.");
      setRevisions(current => ({
        ...current,
        [project.mapId]: data.filter((row): row is MapRevision =>
          !!row && typeof row === "object" &&
          typeof row.revisionNumber === "number" &&
          typeof row.hash === "string" && isMapHash(row.hash)),
      }));
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : "Could not load revisions.");
    }
  }

  async function change(method: string, path: string, message: string, value?: string) {
    setWorking(true); setError(""); setNote("");
    try {
      await send(method, path, value);
      setNote(message);
      setRevisions({});
      setExpanded(null);
      await load();
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : "Creator operation failed.");
    } finally { setWorking(false); }
  }

  function visibility(revision: MapRevision): Visibility {
    return revision.package.draft ? "Draft"
      : revision.package.listed ? "Published" : "Unlisted";
  }

  if (authLoading) return <section className="panel maps-auth"><p>Checking Hunter License…</p></section>;
  if (!session) return <MapAccountPanel />;

  return (
    <section className="maps-creator">
      <div className="maps-creator-bar">
        <h2>Your maps</h2>
        <div className="hero-actions">
          <button className="button button-ghost" type="button" disabled={loading || working}
            onClick={() => void load()}>Refresh</button>
          <Link className="button" href="/maps/upload">Publish Map</Link>
        </div>
      </div>
      {loading && <p role="status">Loading your Community maps…</p>}
      {error && <p role="alert" className="maps-error">{error}</p>}
      {note && <p role="status" className="maps-success">{note}</p>}
      {!loading && projects.length === 0 && !error &&
        <div className="panel maps-message">
          <h3>No maps published yet.</h3>
          <p>Export your first map from Project Prime Studio and upload its .ppmap package.</p>
          <Link className="text-link" href="/maps/upload">Upload your first map →</Link>
        </div>}

      {projects.map(project => (
        <article className="panel maps-owner-card" key={project.mapId}>
          <div className="maps-owner-top">
            <div>
              <span className="mini-tag">{project.archivedAt ? "ARCHIVED" :
                project.deletedAt ? "DELETED" : "MY MAP"}</span>
              <h3>{project.displayName || project.name}</h3>
              <p>{project.revisionCount} revisions · {project.mapId}</p>
            </div>
            <div className="hero-actions">
              <Link className="button button-ghost" href={"/maps/" + project.mapId}>
                Public Page
              </Link>
              {project.canManageLifecycle && !project.deletedAt &&
                <button className="button button-ghost" type="button" disabled={working}
                  onClick={() => {
                    if (project.archivedAt || window.confirm("Archive this map from public discovery?")) {
                      void change("POST", "v2/maps/" + project.mapId +
                        (project.archivedAt ? "/restore" : "/archive"),
                        project.archivedAt ? "Map restored." : "Map archived.");
                    }
                  }}>
                  {project.archivedAt ? "Restore Map" : "Archive Map"}
                </button>}
              <button className="button button-ghost" type="button" disabled={working}
                aria-expanded={expanded === project.mapId}
                onClick={() => void showRevisions(project)}>
                {expanded === project.mapId ? "Hide Revisions" : "Manage Revisions"}
              </button>
            </div>
          </div>
          {expanded === project.mapId && (
            <div className="maps-owner-revisions">
              {(revisions[project.mapId] || []).map(revision => (
                <div className="maps-owner-revision" key={revision.hash}>
                  <div>
                    <strong>Revision {revision.revisionNumber}</strong>
                    <p>{revision.releaseNotes || "No release notes"}</p>
                    <small>{revision.hash.slice(0, 16)}…</small>
                  </div>
                  <div className="maps-owner-controls">
                    <label htmlFor={"visibility-" + revision.hash}>Visibility</label>
                    <select id={"visibility-" + revision.hash} disabled={working}
                      value={visibility(revision)}
                      onChange={event => void change("POST",
                        "packages/" + revision.hash + "/visibility",
                        "Revision visibility updated.", event.target.value)}>
                      <option value="Published">Published</option>
                      <option value="Unlisted">Unlisted</option>
                      <option value="Draft">Draft</option>
                    </select>
                    {project.currentHash !== revision.hash && !project.archivedAt && !project.deletedAt &&
                      <button className="button button-ghost" disabled={working} type="button"
                        onClick={() => {
                          if (window.confirm("Make this historical revision the public current version?")) {
                            void change("POST", "v2/maps/" + project.mapId +
                              "/revisions/" + revision.revisionNumber + "/promote",
                              "Current map revision updated.");
                          }
                        }}>Make Current</button>}
                  </div>
                </div>
              ))}
              {!(revisions[project.mapId] || []).length &&
                <p>Loading revision history or no revisions available.</p>}
            </div>
          )}
        </article>
      ))}
    </section>
  );
}
