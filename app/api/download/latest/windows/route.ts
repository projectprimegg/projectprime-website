import { NextResponse } from "next/server";
import {
  getLatestProjectPrimeRelease,
  getWindowsClientAsset,
} from "@/lib/projectPrimeRelease";

export async function GET() {
  try {
    const release = await getLatestProjectPrimeRelease();
    const asset = getWindowsClientAsset(release);

    if (!asset) {
      return NextResponse.redirect(release.html_url, 307);
    }

    return NextResponse.redirect(asset.browser_download_url, 307);
  } catch {
    return NextResponse.redirect(
      "https://github.com/AntiNotAnti/Project-Prime/releases/latest",
      307,
    );
  }
}
