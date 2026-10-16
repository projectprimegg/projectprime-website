import Link from "next/link";
import { notFound } from "next/navigation";
import {
  bytesLabel, communityApiUrl, getCommunityMap, getCommunityRevisions,
  isMapId, mapTitle, preferredPackage,
} from "@/lib/communityMaps";

export const dynamic = "force-dynamic";

export default async function MapDetails({
  params,
}: {
  params: Promise<{ mapId: string }>;
}) {
  const { mapId } = await params;
  if (!isMapId(mapId)) notFound();

  let map;
  let revisions;
  try {
    [map, revisions] = await Promise.all([
      getCommunityMap(mapId),
      getCommunityRevisions(mapId),
    ]);
  } catch {
    return (
      <main className="page-shell maps-library">
        <div className="panel maps-message">
          <h2>Map service unavailable</h2>
          <p>This map&apos;s details could not be loaded. Try again shortly.</p>
          <Link href="/maps" className="text-link">Return to Maps →</Link>
        </div>
      </main>
    );
  }
  if (!map) notFound();
  const pack = preferredPackage(map);
  const title = mapTitle(map);

  return (
    <main className="page-shell maps-library">
      <Link className="text-link maps-back" href="/maps">← All community maps</Link>
      <section className="map-detail panel">
        <div className="map-detail-art">
          <img alt={"Preview of " + title} src={"/api/maps/previews/" + pack.hash} />
        </div>
        <div className="map-detail-copy">
          <span className="eyebrow">COMMUNITY MAP</span>
          <h1>{title}</h1>
          <p>Created by {map.author || pack.author || "Community creator"}</p>
          <div className="map-specs">
            <span>Players: {pack.minPlayers}–{pack.maxPlayers}</span>
            <span>Package: {bytesLabel(pack.bytes)}</span>
            <span>Revisions: {map.revisionCount}</span>
            <span>Protocol: {pack.minimumProtocol || "Unspecified"}</span>
          </div>
          {pack.supportedModes?.length > 0 &&
            <p>Supported modes: {pack.supportedModes.join(", ")}</p>}
          <div className="hero-actions">
            <a className="button" href={communityApiUrl("packages/" + pack.hash)}>
              Download .ppmap
            </a>
            <Link className="button button-ghost" href="/download">Get Project Prime</Link>
          </div>
          <p className="helper">To install, open Project Prime&apos;s Community browser and
            select this map. Multiplayer hosts synchronize the exact published package.</p>
        </div>
      </section>
      <section className="maps-revisions">
        <h2>Published revisions</h2>
        <div className="panel map-revision-list">
          {revisions.length ? revisions.map((revision) => (
            <div className="map-revision" key={revision.hash}>
              <div>
                <strong>Revision {revision.revisionNumber}</strong>
                <p>{revision.releaseNotes || "No release notes"}</p>
                <small>SHA-256: {revision.hash.slice(0, 16)}…</small>
              </div>
              <a href={communityApiUrl("packages/" + revision.hash)} className="text-link">
                Download →
              </a>
            </div>
          )) : <p>No public revision history available.</p>}
        </div>
      </section>
    </main>
  );
}
