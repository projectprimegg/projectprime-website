import { windowsInstallGuideBase64 } from "@/lib/installGuide";

export const dynamic = "force-static";

export async function GET() {
  const bytes = Buffer.from(windowsInstallGuideBase64, "base64");

  return new Response(bytes, {
    headers: {
      "Content-Type": "image/webp",
      "Cache-Control": "public, max-age=31536000, immutable",
      "Content-Disposition": 'inline; filename="project-prime-windows-install-guide.webp"',
    },
  });
}
