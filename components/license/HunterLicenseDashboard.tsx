"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { MapAccountPanel, useMapAuth } from "@/components/maps/MapAccount";
import {
  achieved, gameModeName, hunterName, isLicenseSnapshot,
  kdRatio, winRate, type HunterLicenseSnapshot,
} from "@/lib/hunterLicense";

function formatCount(value: number) {
  return Number.isFinite(value) ? value.toLocaleString("en-US") : "0";
}

export function HunterLicenseDashboard() {
  const { client, session, loading: authLoading } = useMapAuth();
  const [snapshot, setSnapshot] = useState<HunterLicenseSnapshot | null>(null);
  const [loading, setLoading] = useState(false);
  const [reload, setReload] = useState(0);
  const [error, setError] = useState("");
  const [visibility, setVisibility] = useState<boolean | null>(null);
  const [privacyError, setPrivacyError] = useState("");
  const [privacyBusy, setPrivacyBusy] = useState(false);
  const accessToken = session?.access_token;

  useEffect(() => {
    if (!accessToken) { setSnapshot(null); setVisibility(null); return; }
    const controller = new AbortController();
    let live = true;
    async function load() {
      setLoading(true); setError("");
      try {
        const response = await fetch("/api/license/snapshot", {
          method: "POST",
          headers: { Authorization: "Bearer " + accessToken },
          cache: "no-store",
          signal: controller.signal,
        });
        const data: unknown = await response.json();
        if (!response.ok) {
          const problem = data as { error?: string };
          throw new Error(problem.error || "Could not load your Hunter License.");
        }
        if (!isLicenseSnapshot(data)) throw new Error("Invalid Hunter License response.");
        if (live) setSnapshot(data);

        const preference = await client.rpc("project_prime_hunter_visibility");
        if (!live) return;
        if (preference.error) {
          setPrivacyError("Profile visibility controls are not yet available on this service.");
          setVisibility(null);
        } else {
          setPrivacyError("");
          setVisibility(preference.data === true);
        }
      } catch (failure) {
        if (live && !controller.signal.aborted)
          setError(failure instanceof Error ? failure.message : "License unavailable.");
      } finally { if (live) setLoading(false); }
    }
    void load();
    return () => { live = false; controller.abort(); };
  }, [accessToken, client, reload]);

  async function setPublic(next: boolean) {
    if (!snapshot || visibility === null) return;
    setPrivacyBusy(true); setPrivacyError("");
    const { data, error: failure } = await client.rpc("project_prime_set_hunter_visibility", {
      p_public: next,
    });
    if (failure) setPrivacyError(failure.message);
    else setVisibility(data === true);
    setPrivacyBusy(false);
  }

  if (authLoading) return <div className="panel license-loading">Checking Hunter License sign-in…</div>;
  if (!session) return (
    <div className="license-sign-in">
      <p>Sign in with the same Supabase identity linked to your in-game
        Hunter License. Signing into a different identity starts a separate career.</p>
      <MapAccountPanel purpose="license" redirectTo="/license" />
    </div>
  );

  const profile = snapshot?.profile;
  const stats = snapshot?.stats;
  const rank = profile?.rating_tier != null
    ? "Tier " + profile.rating_tier + " · " + formatCount(profile.rating_points) + " RP"
    : "Unranked";

  return (
    <div className="license-dashboard">
      <div className="license-toolbar">
        <span className="mini-tag">AUTHENTICATED HUNTER LICENSE</span>
        <div className="hero-actions">
          <button className="button button-ghost" onClick={() => setReload(n => n + 1)}
            type="button" disabled={loading}>Refresh Career</button>
          <Link className="button button-ghost" href="/leaderboards">View Leaderboards</Link>
        </div>
      </div>

      {loading && <p className="license-feedback" role="status">Synchronizing with the game career service…</p>}
      {error && <div className="panel maps-message">
        <h2>Hunter License unavailable</h2>
        <p role="alert">{error}</p>
        <button className="button button-ghost" type="button"
          onClick={() => setReload(n => n + 1)}>Retry</button>
      </div>}

      {profile && stats && <>
        <section className="panel license-identity">
          <div>
            <span className="eyebrow">HUNTER LICENSE // VERIFIED ACCOUNT</span>
            <h2>{profile.display_name || "Hunter"}</h2>
            <p>HUNTER ID · #{profile.player_id.replaceAll("-", "").slice(0, 8).toUpperCase()}</p>
            <p>Favorite Hunter · <strong>{hunterName(profile.favorite_hunter)}</strong></p>
            <p>Competitive rating · <strong>{rank}</strong></p>
          </div>
          <div className="license-avatar" aria-hidden="true">
            <span>{hunterName(profile.favorite_hunter).slice(0, 1)}</span>
            <small>{hunterName(profile.favorite_hunter).toUpperCase()}</small>
          </div>
        </section>

        <section className="license-section">
          <div className="license-section-heading">
            <span className="eyebrow">AUTHORITATIVE CAREER</span>
            <h2>Career overview</h2>
            <p>Only accepted eligible online match reports contribute to these totals.</p>
          </div>
          <div className="license-stats">
            {[
              ["Matches", formatCount(stats.games_played)],
              ["Wins", formatCount(stats.wins)],
              ["Losses", formatCount(stats.losses)],
              ["Win Rate", winRate(stats.wins, stats.games_played)],
              ["Kills", formatCount(stats.kills)],
              ["Deaths", formatCount(stats.deaths)],
              ["K/D", kdRatio(stats.kills, stats.deaths)],
              ["Assists", formatCount(stats.assists)],
              ["Headshots", formatCount(stats.headshots)],
              ["Damage", formatCount(stats.damage)],
              ["Best Streak", formatCount(stats.longest_kill_streak)],
              ["Octolith Scores", formatCount(stats.octolith_scores)],
            ].map(([label, value]) => (
              <div className="panel license-stat" key={label}>
                <span>{label}</span><strong>{value}</strong>
              </div>
            ))}
          </div>
        </section>

        <section className="panel license-privacy">
          <span className="mini-tag">PROFILE VISIBILITY</span>
          <h2>Public by default. You're in control.</h2>
          <p>Your Hunter nickname, favorite Hunter and accepted lifetime career
            statistics are public by default. Turn the switch off to opt out:
            other Hunters will no longer see your name or public License profile.
            Your email, account UUID, sign-in details, cosmetics and private match
            history are never published.</p>
          <label className="license-visibility-switch">
            <input type="checkbox" checked={visibility === true}
              disabled={visibility === null || privacyBusy}
              onChange={event => void setPublic(event.target.checked)} />
            <span>Keep my Hunter License publicly visible (turn off to opt out)</span>
          </label>
          {privacyError && <p className="maps-error" role="status">{privacyError}</p>}
          {visibility === true &&
            <p className="maps-success">Your public Hunter profile is visible. Switch off anytime to opt out.</p>}
          {visibility === false &&
            <p className="helper">You opted out. Your name and public profile are hidden; accepted career scores remain anonymous.</p>}
        </section>

        <section className="license-section">
          <div className="license-section-heading">
            <span className="eyebrow">MATCH ARCHIVE</span>
            <h2>Recent matches</h2>
            <p>These match records are visible only while signed into your Hunter License.</p>
          </div>
          <div className="panel license-matches">
            {snapshot.matches.length === 0 ? <p>No accepted match reports yet.</p>
              : snapshot.matches.map(match => (
                <article className="license-match" key={match.match_id}>
                  <div>
                    <strong>{match.room_key || "Unknown arena"}</strong>
                    <p>{gameModeName(match.mode)} · {match.played_at
                      ? new Date(match.played_at).toLocaleDateString("en-US") : "Date unknown"}</p>
                  </div>
                  <div>
                    <span className="license-outcome">{match.tied ? "TIE" :
                      match.won ? "VICTORY" : "DEFEAT"}</span>
                    <small>{match.kills} K / {match.deaths} D / {match.assists} A</small>
                    <small>{match.eligible && match.career_eligible ? "CAREER ELIGIBLE" : "UNRANKED"}</small>
                  </div>
                </article>
              ))}
          </div>
        </section>

        <section className="license-section">
          <div className="license-section-heading">
            <span className="eyebrow">PROGRESSION</span>
            <h2>Career milestones</h2>
          </div>
          <div className="license-milestones">
            {achieved(stats).map(item => (
              <div className="panel license-milestone" key={item.title}>
                <span>{item.title}</span>
                <strong>{item.unlocked ? "UNLOCKED" : "LOCKED"}</strong>
              </div>
            ))}
          </div>
        </section>
      </>}
    </div>
  );
}
