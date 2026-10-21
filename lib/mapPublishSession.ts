export type MapHashesById = Map<string, string>;

// A batch may reuse its owner-scoped catalog snapshot. Upload tickets remain
// per file, so a session change cannot reuse another owner's catalog.
export class MapPublishSession {
  private ownerId: string | null = null;
  private latestHashes: MapHashesById | null = null;

  async latestHashFor(
    ownerId: string,
    mapId: string,
    load: () => Promise<MapHashesById>,
  ): Promise<string | null> {
    if (this.ownerId !== ownerId) {
      this.ownerId = ownerId;
      this.latestHashes = null;
    }
    if (!this.latestHashes) this.latestHashes = await load();
    return this.latestHashes.get(mapId.toLowerCase()) ?? null;
  }

  recordPublished(ownerId: string, mapId: string, hash: string): void {
    if (this.ownerId === ownerId && this.latestHashes) {
      this.latestHashes.set(mapId.toLowerCase(), hash);
    }
  }

  invalidate(): void { this.latestHashes = null; }
}
