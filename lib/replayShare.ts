/** Public link transport only. Never sends .ppdemo files, private paths or credentials. */
const VIDEO_ID = /^[A-Za-z0-9_-]{11}$/;
const HOSTS = new Set(["youtube.com","www.youtube.com","m.youtube.com",
  "music.youtube.com","youtube-nocookie.com","www.youtube-nocookie.com","youtu.be"]);

export function youtubeVideoId(input: string): string | null {
  const text = input.trim();
  if (VIDEO_ID.test(text)) return text;
  if (text.length > 1024) return null;
  let url: URL;
  try { url = new URL(text); } catch { return null; }
  if (url.protocol !== "https:" || url.username || url.password || url.port
      || !HOSTS.has(url.hostname.toLowerCase())) return null;
  const parts = url.pathname.split("/").filter(Boolean);
  const host = url.hostname.toLowerCase();
  let id: string | null = null;
  if (host === "youtu.be" && parts.length === 1) id = parts[0];
  else if (parts.length === 1 && parts[0] === "watch") id = url.searchParams.get("v");
  else if (parts.length === 2 && ["embed","shorts","live"].includes(parts[0])) id = parts[1];
  return id && VIDEO_ID.test(id) ? id : null;
}
export function replayShareTitle(value: string): string {
  return value.trim().replace(/\s+/g," ").slice(0,100);
}
export function replaySharePath(url: string, title = ""): string | null {
  const id = youtubeVideoId(url);
  if (!id) return null;
  const params = new URLSearchParams({video:id});
  const safeTitle = replayShareTitle(title);
  if (safeTitle) params.set("title",safeTitle);
  return "/watch/replays?" + params.toString();
}
