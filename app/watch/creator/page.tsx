import Link from "next/link";
import { ReplayCreatorLinkBuilder } from "@/components/replays/ReplayCreatorLinkBuilder";
export const metadata = { title:"Replay Creator Hub",description:"Create public links to exported Prime Studio videos hosted on YouTube."};
export default function ReplayCreatorPage() {
  return <main className="page-shell replay-watch">
    <section className="page-hero">
      <span className="eyebrow">THEATRE // CREATOR HUB</span>
      <h1>Share the moment.</h1>
      <p>Export a replay highlight and make a public link. Creator links never
        publish or transfer your local game recording.</p>
      <Link className="text-link" href="/watch">Back to Watch</Link>
    </section>
    <section className="replay-steps" aria-label="How to share a replay">
      <article className="panel"><span className="mini-tag">01 · EDIT</span>
        <h2>Open Prime Studio</h2><p>Select a local replay and create a cinematic highlight.</p></article>
      <article className="panel"><span className="mini-tag">02 · EXPORT</span>
        <h2>Render a video</h2><p>Choose your edit range and export the finished footage.</p></article>
      <article className="panel"><span className="mini-tag">03 · SHARE</span>
        <h2>Host and link</h2><p>Upload the video to YouTube and paste its link below.</p></article>
    </section>
    <ReplayCreatorLinkBuilder />
  </main>;
}
