# Installation

Requirement: Jellyfin 12.4 (.NET 10). Tested with 12.4 only.

## Via plugin repository (recommended)
1. Jellyfin → Dashboard → Plugins → Repositories → add
   `https://raw.githubusercontent.com/brewing8309/Youfin/main/manifest.json`
2. Open the plugin catalog, install **Youfin**, restart Jellyfin.

This needs the repository to be public. Updates then appear in the plugin catalog like for any other plugin.

## Manually
1. Download `youfin_<version>.zip` from the [releases](https://github.com/brewing8309/Youfin/releases) and copy `Jellyfin.Plugin.Youfin.dll` to `<Jellyfin data folder>/plugins/Youfin_<version>/`.
2. Restart Jellyfin.

Or build it yourself: `dotnet build -c Release src/Jellyfin.Plugin.Youfin`.

## Open the interface
`https://<server>/Youfin/Web/` in the browser where you are signed in to Jellyfin.
The language follows the browser; you can change it with the selector in the header (see [TRANSLATING.md](TRANSLATING.md)).

For a sidebar entry for all users, add this to `jellyfin-web/config.json` (the path depends on your installation):

```json
"menuLinks": [{ "name": "Youfin", "icon": "smart_display", "url": "/Youfin/Web/" }]
```

## FireTV, Swiftfin and Infuse
Dashboard → Plugins → Youfin → enable *Create a Jellyfin collection per series*, then run the scheduled task
**Youfin: Sync series collections** (it then runs daily). Optionally collections per channel as well.
Concept and data model: [ORGANISATION.md](ORGANISATION.md).

## Updating
Install the update from the catalog, or replace the DLL, and restart Jellyfin. Settings and user data
(`<plugin data folder>/state.json`) are kept. An older `state.json` (schema 1) is migrated automatically; the backup is `state.json.v1.bak`.

## Data
- Video data is read live from Jellyfin (nothing is copied or overwritten).
- Channel name: Jellyfin person (director) → series → `.info.json` → folder name.
- Youfin only stores channel aliases and merges, series, assignments, channel favorites and collections in `state.json`.

## Limits
- The YouTube-like look exists in the web interface only. The apps show collections.
- Autoplay via link is not possible: the interface opens the Jellyfin details page of the video.
- Series and channel management are admin-only; collections belong to each user.
