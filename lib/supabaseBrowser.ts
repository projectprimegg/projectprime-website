"use client";

import { createClient, type SupabaseClient } from "@supabase/supabase-js";

// These are PUBLIC Supabase API values used by the game as well. Never put a
// service_role or secret key in a NEXT_PUBLIC_ variable.
const URL = process.env.NEXT_PUBLIC_SUPABASE_URL ||
  "https://hwcjaygoistufktorbmf.supabase.co";
const PUBLISHABLE_KEY = process.env.NEXT_PUBLIC_SUPABASE_PUBLISHABLE_KEY ||
  "sb_publishable_EVT45OPl638kA_j8vZ0ebg_sw3aWaVz";

let instance: SupabaseClient | null = null;

export function getSupabaseBrowser(): SupabaseClient {
  if (!instance) {
    instance = createClient(URL, PUBLISHABLE_KEY, {
      auth: { persistSession: true, autoRefreshToken: true, detectSessionInUrl: true },
    });
  }
  return instance;
}
