"use client";

import { useEffect, useMemo, useState } from "react";

type DownloadOption = {
  key: "windows" | "linux" | "macos-arm64" | "macos-x64" | "android";
  label: string;
  detail: string;
  button: string;
  filename?: string;
  size?: string;
  url?: string;
};

function detectPlatform(): DownloadOption["key"] | null {
  const ua = navigator.userAgent.toLowerCase();
  const platform = navigator.platform?.toLowerCase() ?? "";

  if (ua.includes("android")) return "android";
  if (ua.includes("win") || platform.includes("win")) return "windows";
  if (ua.includes("linux") && !ua.includes("android")) return "linux";
  if (ua.includes("mac") || platform.includes("mac")) return "macos-arm64";
  return null;
}

export function PlatformDownloads({
  options,
  fallbackUrl,
}: {
  options: DownloadOption[];
  fallbackUrl: string;
}) {
  const [detected, setDetected] = useState<DownloadOption["key"] | null>(null);

  useEffect(() => {
    setDetected(detectPlatform());
  }, []);

  const ordered = useMemo(() => {
    if (!detected) return options;
    return [...options].sort((a, b) => {
      if (a.key === detected) return -1;
      if (b.key === detected) return 1;
      return 0;
    });
  }, [options, detected]);

  return (
    <section className="feature-grid">
      {ordered.map((platform) => {
        const recommended = platform.key === detected;
        return (
          <article
            className={"feature-card download-option" + (recommended ? " recommended-download" : "")}
            key={platform.key}
          >
            <div className="download-option-tags">
              <span className="mini-tag">{platform.label}</span>
              {recommended ? (
                <span className="recommended-tag">Recommended for this device</span>
              ) : null}
            </div>
            <h3>{platform.detail}</h3>
            <p>
              {platform.filename && platform.size
                ? platform.filename + " · " + platform.size
                : "Latest package information is temporarily unavailable."}
            </p>
            <a
              className="button"
              href={platform.url ?? fallbackUrl}
              target={platform.url ? undefined : "_blank"}
              rel={platform.url ? undefined : "noreferrer"}
            >
              {platform.url ? platform.button : "View Latest Release"}
            </a>
          </article>
        );
      })}
    </section>
  );
}
