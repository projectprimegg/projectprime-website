export const projectPrimeVideos = [
  { id: "Npdzn9HC-b0", url: "https://youtu.be/Npdzn9HC-b0" },
  { id: "ROErLp4ca5w", url: "https://youtu.be/ROErLp4ca5w" },
  { id: "zQsoELTXcvI", url: "https://youtu.be/zQsoELTXcvI" },
  { id: "GE1bLHuVOjs", url: "https://youtu.be/GE1bLHuVOjs" },
  { id: "b3dII6EbRgs", url: "https://youtu.be/b3dII6EbRgs" },
  { id: "-CmixS_6Kkc", url: "https://youtu.be/-CmixS_6Kkc" },
] as const;

export type ProjectPrimeVideo = (typeof projectPrimeVideos)[number];

type OEmbed = {
  title?: string;
  author_name?: string;
};

export async function getVideoMetadata(video: ProjectPrimeVideo) {
  try {
    const endpoint = new URL("https://www.youtube.com/oembed");
    endpoint.searchParams.set("url", `https://www.youtube.com/watch?v=${video.id}`);
    endpoint.searchParams.set("format", "json");

    const response = await fetch(endpoint, { next: { revalidate: 3600 } });
    if (!response.ok) throw new Error("YouTube oEmbed request failed");

    const data = (await response.json()) as OEmbed;
    return {
      ...video,
      title: data.title ?? "Project Prime video",
      author: data.author_name ?? "YouTube",
    };
  } catch {
    return { ...video, title: "Project Prime video", author: "YouTube" };
  }
}
