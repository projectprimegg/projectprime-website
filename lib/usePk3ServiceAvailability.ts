"use client";

import { useEffect, useState } from "react";
import { isPk3ConverterReady } from "@/lib/pk3Contract";

export type Pk3ServiceStatus = "checking" | "available" | "unavailable";

export function usePk3ServiceAvailability() {
  const [status, setStatus] = useState<Pk3ServiceStatus>("checking");
  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    const abort = new AbortController();
    let active = true;
    // Fail closed when the server is offline, the proxy is misrouted or CORS
    // rejects the site's Origin. No source bytes are uploaded on a failed probe.
    const timeout = setTimeout(() => abort.abort(), 6500);
    void isPk3ConverterReady(fetch, abort.signal).then(ready => {
      if (active) setStatus(ready ? "available" : "unavailable");
    }).finally(() => clearTimeout(timeout));
    return () => {
      active = false;
      abort.abort();
      clearTimeout(timeout);
    };
  }, [attempt]);

  return {
    status,
    retry: () => {
      setStatus("checking");
      setAttempt(current => current + 1);
    },
  };
}
