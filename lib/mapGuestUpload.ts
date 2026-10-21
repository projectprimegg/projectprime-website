"use client";

import type { Session, SupabaseClient } from "@supabase/supabase-js";

// Keep the existing Community authorization boundary intact: the browser
// obtains a real Supabase guest session, then exchanges its JWT for the same
// short-lived, owner-scoped upload ticket as a registered Hunter License.
// Never mint a master uploader token or post anonymously to Community.
export async function ensureMapUploadIdentity(
  client: SupabaseClient,
  knownSession: Session | null,
): Promise<void> {
  if (knownSession) return;

  // A login in another tab or an auth-state event may have established a
  // registered session since this component last rendered.
  const { data: existing, error: readError } = await client.auth.getSession();
  if (readError) {
    throw new Error("Could not check your current session. Try again.");
  }
  if (existing.session) return;

  // Only create guests when the visitor explicitly submits a validated map;
  // merely visiting the website or selecting a file must not create users.
  const { data, error } = await client.auth.signInAnonymously();
  if (error || !data.session) {
    throw new Error("Guest uploads are temporarily unavailable. Try again or sign in with a Hunter License.");
  }
}
