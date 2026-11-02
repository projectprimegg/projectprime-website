"use client";

import { useMemo, useState } from "react";
import { replaySharePath } from "@/lib/replayShare";

export function ReplayCreatorLinkBuilder() {
  const [video,setVideo] = useState("");
  const [title,setTitle] = useState("");
  const [copied,setCopied] = useState(false);
  const path = useMemo(()=>replaySharePath(video,title),[video,title]);
  const address = path ? "https://projectprime.gg"+path : "";
  async function copy() {
    if (!address) return;
    try { await navigator.clipboard.writeText(address); setCopied(true); }
    catch { setCopied(false); }
  }
  return <section className="panel replay-share-panel" aria-labelledby="replay-share-heading">
    <span className="mini-tag">PUBLIC VIDEO LINK</span>
    <h2 id="replay-share-heading">Create a replay share link.</h2>
    <p>Export your own highlight in Prime Studio, upload the finished video to YouTube
      as public or unlisted, then paste its link here. This site does not upload
      .ppdemo files, authenticate creators or host video bytes.</p>
    <div className="replay-share-grid">
      <label htmlFor="replay-video">YouTube URL or video ID
        <input id="replay-video" type="text" value={video}
          maxLength={1024} placeholder="https://www.youtube.com/watch?v=…"
          onChange={e=>{setVideo(e.target.value);setCopied(false);}} />
      </label>
      <label htmlFor="replay-title">Display title (optional)
        <input id="replay-title" type="text" value={title}
          maxLength={100} placeholder="Final round highlight"
          onChange={e=>{setTitle(e.target.value);setCopied(false);}} />
      </label>
    </div>
    {video.trim() && !path && <p className="replay-share-error" role="alert">
      Enter a supported HTTPS YouTube, YouTube Shorts or youtu.be link.
    </p>}
    {path && <div className="replay-share-result">
      <label htmlFor="replay-share-link">Shareable link
        <input id="replay-share-link" type="text" value={address} readOnly
          onFocus={e=>e.currentTarget.select()} />
      </label>
      <div className="hero-actions">
        <button className="button" type="button" onClick={copy}>{copied?"Copied":"Copy link"}</button>
        <a className="button button-ghost" href={path} target="_blank"
          rel="noopener noreferrer">Preview link</a>
      </div>
    </div>}
    <p className="replay-share-privacy">Only the video ID and optional display title
      enter your shareable link. Local replay paths, rosters and recording files
      are never sent by this form.</p>
  </section>;
}
