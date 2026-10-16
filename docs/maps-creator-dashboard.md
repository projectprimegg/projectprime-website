# Website creator map management

`/maps/my` uses the game's existing authenticated v2 map catalog and
revision endpoints. No new owner database or duplicate revision history is
stored by the website.

- Owned maps: `GET /v2/maps?mine=true&pageSize=100`
- Revision history: `GET /v2/maps/{mapId}/revisions`
- Archive / restore: `POST /v2/maps/{mapId}/archive` and `/restore`
- Revision visibility: `POST /packages/{sha}/visibility` with JSON string
  `"Published"`, `"Unlisted"`, or `"Draft"`
- Make current: `POST /v2/maps/{mapId}/revisions/{number}/promote`

Each write mints a fresh short-lived Hunter License-derived ticket. The map
service enforces creator ownership and returns the authoritative catalog.
The UI asks for confirmation before archiving or promoting a historical
revision and does not implement irreversible deletion.

In a large creator catalog, the current 100-project page bound should be
replaced by authenticated paging before broad public release.
