import Link from "next/link";

export function ComingSoon({ eyebrow, title, description }: { eyebrow: string; title: string; description: string }) {
  return (
    <main className="page-shell">
      <section className="coming-soon panel">
        <span className="eyebrow">{eyebrow}</span>
        <h1>{title}</h1>
        <p>{description}</p>
        <div className="coming-badge">COMING SOON</div>
        <Link className="button button-ghost" href="/">Return Home</Link>
      </section>
    </main>
  );
}
