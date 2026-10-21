const MAX_RETRY_AFTER_MS = 30_000;

export function mapUploadRetryDelay(
  response: Pick<Response, "headers">,
  retry: number,
  now = Date.now(),
): number {
  const header = response.headers.get("Retry-After")?.trim();
  if (header) {
    const seconds = Number(header);
    if (/^\d+(?:\.\d+)?$/.test(header) && Number.isFinite(seconds)) {
      return Math.min(MAX_RETRY_AFTER_MS, Math.ceil(seconds * 1000));
    }
    const timestamp = Date.parse(header);
    if (Number.isFinite(timestamp)) {
      return Math.min(MAX_RETRY_AFTER_MS, Math.max(0, timestamp - now));
    }
  }
  return Math.min(3000, 200 * retry * retry);
}

export function waitForMapUploadRetry(ms: number, signal: AbortSignal): Promise<void> {
  if (signal.aborted) return Promise.reject(new DOMException("Upload paused.", "AbortError"));
  return new Promise((resolve, reject) => {
    const finish = () => {
      signal.removeEventListener("abort", abort);
      resolve();
    };
    const abort = () => {
      clearTimeout(timer);
      signal.removeEventListener("abort", abort);
      reject(new DOMException("Upload paused.", "AbortError"));
    };
    const timer = setTimeout(finish, Math.max(0, ms));
    signal.addEventListener("abort", abort, { once: true });
    if (signal.aborted) abort();
  });
}
