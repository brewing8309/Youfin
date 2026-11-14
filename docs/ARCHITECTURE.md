# Architecture

## Goals
Show locally stored YouTube videos like a personal offline YouTube inside Jellyfin, reuse existing metadata,
store as little extra data as possible, and work with several thousand videos.

## Findings (Jellyfin 12.4, .NET 10)
- Plugins are .NET assemblies: `BasePlugin<T>`, `IHasWebPages`, `IPluginServiceRegistrator`, own ASP.NET controllers,
  `ILibraryManager`, `IUserDataManager`, `ICollectionManager`, `IScheduledTask`.
- `EnableInMainMenu` plugin pages appear for admins only. There is no official user-facing page in jellyfin-web.
- FireTV, Swiftfin and Infuse cannot show plugin pages. They can show Jellyfin collections.
- The *YouTube Metadata* plugin stores title, description, date and thumbnail, the video ID in `ProviderIds["YoutubeMetadata"]`
  and the channel as a person (director). Tags and categories exist only in the `.info.json`.

## Decisions
1. **Two output layers, one core.** Web interface served by the plugin at `/Youfin/Web/` (embedded resources, same origin, uses the
   Jellyfin login token) plus Jellyfin collections for the native apps.
   Playback opens the Jellyfin details page (`/web/#/details?id=…`).
   **In-Jellyfin view:** Jellyfin has no official way to add a page for normal users, so the plugin registers an `IStartupFilter` middleware
   that adds one script tag to `/web/index.html` (no extra plugin needed). The script (`inject.js`) watches the route; when the opened library
   (`topParentId`/`parentId`) is one of the libraries chosen in the settings it shows the Youfin interface in an iframe below the first header row
   (`?embed=1`). Safeguards: it only touches `index.html` when libraries are chosen and the option is on, it never throws into jellyfin-web, and the user
   can switch back to Jellyfin's own view. Risk: it depends on jellyfin-web's route and header structure, which may change between Jellyfin versions.
2. **Libraries are selectable.** The scanner uses `InternalItemsQuery.AncestorIds` with the libraries from the settings (empty = all); changing the selection rebuilds the index through `ConfigurationChanged`.
3. **No duplicate metadata.** Titles, images, durations, dates, resume position, watched and video favorites come live from Jellyfin.
   Youfin never writes to video items.
4. **Own storage** is a small JSON file (`state.json`, written atomically): channel aliases/merges, series, per-video overrides,
   per-user favorites and collections. See [ORGANISATION.md](ORGANISATION.md).
5. **Index = scan + overlay.** The scanner reads the library (read-only) into immutable records. Merges, aliases, series and
   overrides are applied as an overlay when the index is built, so changing them needs no library rescan. The index is swapped atomically.
6. **Channel detection** (priority): channel ID of the director person → series ID → `.info.json` `channel_id` → channel name → "Unknown".
7. **Freshness:** `ILibraryManager` events (debounced rebuild), plus an admin endpoint to rebuild.
8. **Robustness:** every source is optional; errors per item are logged and skipped. The plugin works without *YouTube Metadata*.
9. **Performance:** in-memory index, paginated endpoints, lazy-loaded thumbnails. Tested with 20,000 videos.
10. **Internationalisation:** the interface texts live in `i18n/<code>.json` (English is the fallback); see [TRANSLATING.md](TRANSLATING.md).

## Components
```
src/Jellyfin.Plugin.Youfin/
  Core/       YouTubeIdParser, InfoJsonReader, ChannelResolver, VideoIndex, models   (no Jellyfin dependency, unit-tested)
  Data/       UserDataStore (state.json), schema 2 + migration from schema 1
  Services/   LibraryScanner, IndexService, CollectionSyncTask
  Api/        controllers: videos/channels, series, collections
  Web/        interface (index.html, app.js, style.css, i18n/) and the plugin settings page
assets/       logo and icons (embedded)
tests/        xUnit
```

## Verified against Jellyfin 12.4
Scan, API, authorisation (401/403), resume/recent/favorites via `InternalItemsQuery`, deep link to the details page, `menuLinks`,
collection sync (create, add, remove), several users, restart persistence.
