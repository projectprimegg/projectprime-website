import { getVideoMetadata, projectPrimeVideos } from "@/lib/videos";

export const metadata = { title: "Media" };

export default async function MediaPage() {
  const videos = await Promise.all(projectPrimeVideos.map(getVideoMetadata));

  return (
    <main className="page-shell">
      <section className="page-hero">
        <span className="eyebrow">PROJECT PRIME MEDIA</span>
        <h1>Watch Project Prime.</h1>
        <p>
          Officially released Project Prime videos, development showcases and gameplay footage.
        </p>
      </section>

      <section className="video-grid">
        {videos.map((video) => (
          <article className="video-card panel" key={video.id}>
            <div className="video-frame">
              <iframe
                src={`https://www.youtube-nocookie.com/embed/${video.id}`}
                title={video.title}
                loading="lazy"
                allow="accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture; web-share"
                allowFullScreen
              />
            </div>
            <div className="video-card-copy">
              <span className="mini-tag">YOUTUBE</span>
              <h2>{video.title}</h2>
              <p>{video.author}</p>
              <a className="text-link" href={video.url} target="_blank" rel="noreferrer">
                Watch on YouTube →
              </a>
            </div>
          </article>
        ))}
      </section>
    </main>
  );
}
