"use client";

import Link from "next/link";
import { useEffect, useMemo, useState, type FormEvent } from "react";
import type { Session } from "@supabase/supabase-js";
import { getSupabaseBrowser } from "@/lib/supabaseBrowser";

export function useMapAuth() {
  const client = useMemo(getSupabaseBrowser, []);
  const [session, setSession] = useState<Session | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let active = true;
    const { data } = client.auth.onAuthStateChange((_event, current) => {
      if (active) {
        setSession(current);
        setLoading(false);
      }
    });
    void client.auth.getSession().then(({ data: result }) => {
      if (active) { setSession(result.session); setLoading(false); }
    }).catch(() => {
      if (active) setLoading(false);
    });
    return () => { active = false; data.subscription.unsubscribe(); };
  }, [client]);

  return { client, session, loading };
}

export function MapAccountPanel() {
  const { client, session, loading } = useMapAuth();
  const [email, setEmail] = useState("");
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState("");
  const [error, setError] = useState("");

  async function sendEmail(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setError(""); setMessage(""); setBusy(true);
    try {
      const { error: failure } = await client.auth.signInWithOtp({
        email: email.trim(),
        options: { emailRedirectTo: window.location.origin + "/maps/account" },
      });
      if (failure) throw failure;
      setMessage("Check your inbox for a sign-in link, then return to publish.");
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : "Unable to send sign-in email.");
    } finally { setBusy(false); }
  }

  async function guest() {
    setBusy(true); setError(""); setMessage("");
    try {
      const { error: failure } = await client.auth.signInAnonymously();
      if (failure) throw failure;
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : "Guest sign-in unavailable.");
    } finally { setBusy(false); }
  }

  async function signOut() {
    setBusy(true);
    const { error: failure } = await client.auth.signOut();
    if (failure) setError(failure.message);
    setBusy(false);
  }

  if (loading) return <div className="panel maps-auth"><p>Checking Hunter License session…</p></div>;
  return (
    <section className="panel maps-auth">
      {session ? (
        <>
          <span className="mini-tag">HUNTER LICENSE CONNECTED</span>
          <h2>{session.user.email || "Guest Hunter"}</h2>
          {session.user.is_anonymous &&
            <p>Guest accounts are tied to this browser until linked. Link a
              permanent identity before relying on cross-device map ownership.</p>}
          <div className="hero-actions">
            <Link className="button" href="/maps/upload">Publish Map</Link>
            <Link className="button button-ghost" href="/maps/my">My Maps</Link>
            <button className="button button-ghost" type="button" onClick={signOut}
              disabled={busy}>Sign Out</button>
          </div>
        </>
      ) : (
        <>
          <span className="mini-tag">CREATOR ACCESS</span>
          <h2>Sign in to publish.</h2>
          <p>Use an account linked to your game Hunter License to manage the
            same maps across the website and Project Prime.</p>
          <form onSubmit={sendEmail}>
            <label htmlFor="map-email">Email address</label>
            <div className="maps-auth-row">
              <input id="map-email" type="email" value={email} maxLength={320}
                required autoComplete="email" placeholder="you@example.com"
                onChange={event => setEmail(event.target.value)} />
              <button className="button" disabled={busy} type="submit">Email Sign-In Link</button>
            </div>
          </form>
          <div className="maps-auth-alternate">
            <p>Want to try publishing without an email? Guest ownership can be
              lost if you clear your browser data.</p>
            <button className="button button-ghost" disabled={busy} onClick={guest}>
              Continue as Guest
            </button>
          </div>
        </>
      )}
      {message && <p className="maps-success" role="status">{message}</p>}
      {error && <p className="maps-error" role="alert">{error}</p>}
    </section>
  );
}
