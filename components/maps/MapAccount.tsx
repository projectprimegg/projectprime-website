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

export function MapAccountPanel({
  purpose = "maps",
  redirectTo = "/maps/account",
}: {
  purpose?: "maps" | "license";
  redirectTo?: "/maps/account" | "/license";
}) {
  const isLicense = purpose === "license";
  const { client, session, loading } = useMapAuth();
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState("");
  const [error, setError] = useState("");

  async function sendEmail(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setError(""); setMessage(""); setBusy(true);
    try {
      if (password) {
        const { error: failure } = await client.auth.signInWithPassword({
          email: email.trim(), password,
        });
        if (failure) throw failure;
        setPassword("");
      } else {
        const { error: failure } = await client.auth.signInWithOtp({
          email: email.trim(),
          options: { emailRedirectTo: window.location.origin + redirectTo },
        });
        if (failure) throw failure;
        setMessage("Check your inbox for the secure Hunter License sign-in link.");
      }
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : "Unable to send sign-in email.");
    } finally { setBusy(false); }
  }

  async function oauth(provider: "google" | "github" | "discord") {
    setBusy(true); setError(""); setMessage("");
    try {
      const { error: failure } = await client.auth.signInWithOAuth({
        provider,
        options: { redirectTo: window.location.origin + redirectTo },
      });
      if (failure) throw failure;
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : "Identity provider is unavailable.");
      setBusy(false);
    }
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
            <Link className="button" href={isLicense ? "/license" : "/maps/upload"}>
              {isLicense ? "View Hunter License" : "Publish Map"}
            </Link>
            <Link className="button button-ghost" href={isLicense ? "/maps/my" : "/license"}>
              {isLicense ? "My Maps" : "My Hunter License"}
            </Link>
            <button className="button button-ghost" type="button" onClick={signOut}
              disabled={busy}>Sign Out</button>
          </div>
        </>
      ) : (
        <>
          <span className="mini-tag">HUNTER LICENSE // SUPABASE IDENTITY</span>
          <h2>{isLicense ? "Sign in to your License." : "Sign in to publish."}</h2>
          <p>Use the same account or identity provider linked to Project Prime in-game.
            A new email or provider identity does not automatically merge an existing guest License.</p>
          <form onSubmit={sendEmail}>
            <label htmlFor="map-email">Email address</label>
            <div className="maps-auth-row">
              <input id="map-email" type="email" value={email} maxLength={320}
                required autoComplete="email" placeholder="you@example.com"
                onChange={event => setEmail(event.target.value)} />
              <button className="button" disabled={busy} type="submit">
                {password ? "Sign In" : "Email Sign-In Link"}
              </button>
            </div>
            <label htmlFor="map-password">Password (optional if previously configured in-game)</label>
            <input id="map-password" type="password" autoComplete="current-password"
              value={password} onChange={event => setPassword(event.target.value)} />
          </form>
          <p className="helper">Or use an identity provider already linked to your License:</p>
          <div className="hero-actions">
            <button className="button button-ghost" type="button"
              disabled={busy} onClick={() => void oauth("google")}>Google</button>
            <button className="button button-ghost" type="button"
              disabled={busy} onClick={() => void oauth("github")}>GitHub</button>
            <button className="button button-ghost" type="button"
              disabled={busy} onClick={() => void oauth("discord")}>Discord</button>
          </div>
          {process.env.NEXT_PUBLIC_ENABLE_GUEST_PUBLISHING === "1" && (
            <div className="maps-auth-alternate">
              <p>Guest publishing is enabled for this deployment. Guest ownership
                can be lost when you clear your browser data.</p>
              <button className="button button-ghost" disabled={busy} onClick={guest}>
                Continue as Guest
              </button>
            </div>
          )}
        </>
      )}
      {message && <p className="maps-success" role="status">{message}</p>}
      {error && <p className="maps-error" role="alert">{error}</p>}
    </section>
  );
}
