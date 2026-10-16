import Link from "next/link";

const nav = [
  ["Game", "/game"],
  ["Download", "/download"],
  ["Maps", "/maps"],
  ["Hunters", "/hunters"],
  ["Leaderboards", "/leaderboards"],
  ["Watch", "/watch"],
  ["Community", "/community"],
] as const;

export function SiteHeader() {
  return (
    <header className="site-header">
      <Link href="/" className="brand" aria-label="Project Prime home">
        <span className="brand-mark">P</span>
        <span>PROJECT PRIME</span>
      </Link>
      <nav className="main-nav" aria-label="Primary navigation">
        {nav.map(([label, href]) => (
          <Link key={href} href={href}>{label}</Link>
        ))}
      </nav>
      <Link className="button button-small" href="/download">Play Now</Link>
    </header>
  );
}
