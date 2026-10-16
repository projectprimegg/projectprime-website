"use client";

import { useEffect, useState } from "react";
import { hunterAuthReturnError } from "@/lib/hunterAuthRedirect";

export function AuthReturnNotice() {
  const [message, setMessage] = useState<string | null>(null);
  useEffect(() => { setMessage(hunterAuthReturnError(window.location.hash)); }, []);
  if (!message) return null;
  return (
    <div className="panel maps-message" role="alert">
      <h2>Hunter verification link needs a retry</h2>
      <p>{message}</p>
      <p>Request a new email link below, or resend verification from the game's Hunter License account tab.</p>
    </div>
  );
}
