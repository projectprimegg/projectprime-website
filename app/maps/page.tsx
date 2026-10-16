import Link from "next/link";
import {
  browseCommunityMaps, bytesLabel, mapTitle, preferredPackage,
  type MapBrowse,
} from "@/lib/communityMaps";

export const metadata = {
  title: "Community Maps",
  description: "Discover, download and publish custom Project Prime multiplayer maps.",
};
export const dynamic = "force-dynamic";

type Search = {
  query?: string;
  sort?: string;
  mode?: string;
  page?: string;
};

function buildHref(search: Search, page: number) {
  const params = new URLSearchParams();
  if (search.query) params.set("query", search.query);
  if (search.mode) params.set("mode", search.mode);
  if (search.sort) params.set("sort", search.sort);
  params.set("page", String(page));
  return "/maps?" + params.toString();
}

export default async function MapsPage({
  searchParams,
}: {
  searchParams: Promise<Search>;
}) {
  const search = await searchParams;
  const page = Math.max(1, Math.min(2000, Number(search.page) || 1));
  let catalog: MapBrowse = { items: [], total: 0 };
  let error = false;
  try {
    catalog = await browseCommunityMaps({
      query: search.query?.trim(),
      sort: search.sort,
      mode: search.mode,
      page,
    });
  } catch {
    error = true;
  }

  return (
    <main className="page-shell maps-library">
      <section className="page-hero maps-hero">
        <span className="eyebrow">COMMUNITY CONTENT // MAP LIBRARY</span>
        <h1>Find your next arena.</h1>
        <p>Discover maps made by the community, download an exact playable
          package, or share your own creation with Project Prime players.</p>
        <div className="hero-actions">
          <Link href="/maps/upload" className="button">Publish a Map</Link>
          <Link href="/maps/my" className="button button-ghost">My Maps</Link>
        </div>
      </section>

      <form className="maps-search panel" action="/maps" method="get">
        <label htmlFor="map-query">Search maps and creators</label>
        <div className="maps-filters">
          <input id="map-query" name="query" type="search" maxLength={120}
            defaultValue={search.query || ""} placeholder="Search maps or creators" />
          <select name="sort" aria-label="Sort maps" defaultValue={search.sort || "updated"}>
            <option value="updated">Recently updated</option>
            <option value="new">Newest</option>
            <option value="name">Name</option>
            <option value="favorites">Most favorited</option>
          </select>
          <button className="button button-small" type="submit">Find maps</button>
        </div>
      </form>

      <div className="maps-results-heading">
        <h2>Community maps</h2>
        {!error && <span>{catalog.total} published maps</span>}
      </div>

      {error ? (
        <section className="panel maps-message" role="status">
          <h3>Map catalog temporarily unavailable</h3>
          <p>We couldn&apos;t reach the Community service. Your installed maps
            and existing multiplayer lobbies are unaffected.</p>
          <Link className="text-link" href="/maps">Try again →</Link>
        </section>
      ) : catalog.items.length === 0 ? (
        <section className="panel maps-message">
          <h3>No maps found</h3>
          <p>Try another search, or become the first creator to publish a map.</p>
          <Link className="text-link" href="/maps/upload">Publish a map →</Link>
        </section>
      ) : (
        <div className="maps-grid">
          {catalog.items.map((project) => {
            const pack = preferredPackage(project);
            return (
              <article className="map-listing panel" key={project.mapId}>
                <Link href={"/maps/" + project.mapId} className="map-cover"
                  aria-label={"View " + mapTitle(project)}>
                  <img alt="" loading="lazy" src={"/api/maps/previews/" + pack.hash} />
                  <span className="map-cover-tag">COMMUNITY</span>
                </Link>
                <div className="map-listing-body">
                  <span className="mini-tag">{pack.supportedModes?.length
                    ? pack.supportedModes.join(" • ") : "CUSTOM ARENA"}</span>
                  <h3><Link href={"/maps/" + project.mapId}>{mapTitle(project)}</Link></h3>
                  <p>by {project.author || pack.author || "Community creator"}</p>
                  <div className="map-listing-details">
                    <span>{pack.minPlayers}–{pack.maxPlayers} players</span>
                    <span>{bytesLabel(pack.bytes)}</span>
                  </div>
                </div>
              </article>
            );
          })}
        </div>
      )}

      {!error && catalog.total > 24 && (
        <nav className="maps-pagination" aria-label="Map catalog pages">
          {page > 1
            ? <Link className="button button-ghost" href={buildHref(search, page - 1)}>Previous</Link>
            : <span />}
          <span>Page {page} of {Math.ceil(catalog.total / 24)}</span>
          {page * 24 < catalog.total
            ? <Link className="button button-ghost" href={buildHref(search, page + 1)}>Next</Link>
            : <span />}
        </nav>
      )}
    </main>
  );
}
