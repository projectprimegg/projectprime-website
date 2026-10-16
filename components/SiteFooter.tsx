import Link from "next/link";

export function SiteFooter() {
  return (
    <footer className="site-footer">
      <div>
        <strong>PROJECT PRIME</strong>
        <p>Built by and for the Metroid Prime Hunters community.</p>
      </div>
      <div className="footer-links">
        <Link href="/game">Game</Link>
        <Link href="/download">Download</Link>
        <a href="https://discord.gg/sudbyZMZh3" target="_blank" rel="noreferrer">Discord</a>
        <Link href="/community">Community</Link>
      </div>
      <p className="legal">Project Prime is a community project. Additional legal and attribution information will be added before public launch.</p>
    </footer>
  );
}
