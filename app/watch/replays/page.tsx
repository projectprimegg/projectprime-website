import Link from "next/link";
import { youtubeVideoId,replayShareTitle } from "@/lib/replayShare";
export const metadata = {title:"Community Replay Highlight",description:"Watch a creator-shared Project Prime replay video."};
type Parameters = {video?:string|string[];title?:string|string[]};
export default async function SharedReplayPage({searchParams}:{searchParams:Promise<Parameters>}) {
  const q = await searchParams;
  const video = youtubeVideoId(typeof q.video === "string" ? q.video : "");
  const title = replayShareTitle(typeof q.title === "string" ? q.title : "") || "Community replay highlight";
  return <main className="page-shell replay-watch">
    <section className="page-hero">
      <span className="eyebrow">THEATRE // COMMUNITY VIDEO</span>
      <h1>{video ? title : "Replay link unavailable."}</h1>
      <p>{video
        ? "This is a creator-shared YouTube video, not a verified match result or an uploaded game recording."
        : "This link needs a valid YouTube video ID. Nothing was uploaded or opened from your device."}</p>
    </section>
    {video ? <section className="panel replay-embed">
      <div className="video-frame">
        <iframe src={"https://www.youtube-nocookie.com/embed/"+video} title={title}
          loading="lazy" referrerPolicy="strict-origin-when-cross-origin"
          allow="accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture; web-share"
          allowFullScreen />
      </div>
      <div className="hero-actions">
        <a className="button button-ghost" href={"https://www.youtube.com/watch?v="+video}
          target="_blank" rel="noopener noreferrer">Open on YouTube</a>
        <Link className="button button-ghost" href="/watch/creator">Share your own</Link>
      </div>
    </section> : <section className="panel replay-watch-card">
      <Link className="button" href="/watch/creator">Create a valid link</Link>
    </section>}
  </main>;
}
