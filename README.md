<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="assets/logo-dark.svg">
    <img alt="Youfin" src="assets/logo-light.svg" height="72">
  </picture>
</p>

<h3 align="center">Your private offline YouTube for Jellyfin.</h3>

<p align="center">
  Turn downloaded YouTube videos into a library that feels like YouTube:<br>
  channels, series, standalone videos, collections and search. Local, no algorithm, no internet needed.
</p>

<p align="center">
  <a href="https://github.com/brewing8309/Youfin/actions/workflows/build.yml"><img alt="Build" src="https://github.com/brewing8309/Youfin/actions/workflows/build.yml/badge.svg"></a>
  <img alt="Version" src="https://img.shields.io/badge/version-0.1.2-ffb547">
  <img alt="Jellyfin 12.4" src="https://img.shields.io/badge/Jellyfin-12.4-00a4dc?logo=jellyfin&logoColor=white">
  <img alt=".NET 10" src="https://img.shields.io/badge/.NET-10-512bd4?logo=dotnet&logoColor=white">
  <a href="LICENSE"><img alt="License: GPL-3.0" src="https://img.shields.io/badge/license-GPL--3.0-blue"></a>
</p>

<p align="center">
  <img alt="Youfin home page" src="docs/screenshots/home.png" width="860">
</p>

> 🤖 **100% vibecoded:** Youfin was created entirely with AI (Claude Code by Anthropic), from the idea to the logo. The code is tested but was not written by hand. Use at your own risk.
>
> Screenshots show demo data. The web interface is available in English and German and is easy to extend with more languages, see [Languages](#languages).

## Why Youfin?

Downloaded YouTube videos don't fit well into Jellyfin's movies-and-shows structure. Sometimes it's a whole channel, sometimes a single video you just want to keep. Youfin gives both a proper place, without forcing you to create artificial series.

- **Uses what is already there.** Title, description, thumbnail and date come from Jellyfin or the *YouTube Metadata* plugin. Youfin downloads nothing and overwrites nothing.
- **Standalone videos are first-class.** A video needs neither a series nor a channel collection to be a full part of the library.
- **Multi-user.** Continue watching, favorites and collections are separate per user.
- **Fast.** The index lives in memory and lists load page by page. Tested with 20,000 videos.

## Features

| | |
|---|---|
| **Home** | Continue watching, next episode of your series, new standalone videos, watch later, my channels, from the archive |
| **Channels** | Detected automatically (channel ID, otherwise name), favorites, rename and merge without touching the original metadata |
| **Series** | Ordered series across channel boundaries, optionally with a channel rule (new videos join automatically), progress and "next episode" |
| **Standalone videos** | Everything that is not part of a series, with its own overview |
| **Collections** | Free-form groupings per user, manually sortable, including "Watch later" |
| **Search** | Title, channel, description, YouTube ID, tags and categories |
| **Management** | Via the ⋯ menu and multi-select: assign to a series, mark as standalone, add to a collection |
| **Playback** | With the native Jellyfin player; progress and "watched" stay in Jellyfin |
| **Your libraries only** | Choose which Jellyfin libraries Youfin uses, so your normal movies and shows stay out |
| **Inside Jellyfin** | Opening one of your chosen libraries in the Jellyfin web client shows the Youfin interface right there |

<table>
  <tr>
    <td><img alt="Series" src="docs/screenshots/series.png"></td>
    <td><img alt="Standalone videos with menu" src="docs/screenshots/singles.png"></td>
  </tr>
  <tr>
    <td><img alt="Video page" src="docs/screenshots/video.png"></td>
    <td><img alt="Channels" src="docs/screenshots/channels.png"></td>
  </tr>
</table>

## The concept

| Concept | Meaning |
|---|---|
| **Channel** | Where the video came from, detected automatically. Always kept as metadata. |
| **Series** | A curated, ordered series. A video belongs to at most one series. |
| **Collection** | Your own free-form grouping. A video can be in several. |
| **Standalone video** | A video without a series. This is the default for new videos. |

More in [docs/ORGANISATION.md](docs/ORGANISATION.md).

## Installation

**Requirement:** Jellyfin 12.4 (.NET 10).

**Via plugin repository** (recommended; this repository is a Jellyfin plugin repository, it needs to be public):
1. Jellyfin → Dashboard → Plugins → Repositories → add: `https://raw.githubusercontent.com/brewing8309/Youfin/main/manifest.json`
2. Open the plugin catalog, install *Youfin* and restart Jellyfin. Updates show up in the catalog.

**Manually:**
1. Download `youfin_<version>.zip` from the [releases](https://github.com/brewing8309/Youfin/releases) and copy the DLL to `<Jellyfin data folder>/plugins/Youfin_<version>/`.
2. Restart Jellyfin.

**Choose your libraries:** Dashboard → Plugins → Youfin → select the libraries that contain your YouTube videos. Without a selection Youfin uses all libraries (the interface reminds admins to choose).

**Use it inside Jellyfin:** with the default setting *Show the Youfin interface when one of these libraries is opened*, clicking such a library in the Jellyfin web client shows Youfin below the Jellyfin header. A button *Jellyfin view* switches back to the normal library view (and *Open Youfin view* returns). Playing a video opens the normal Jellyfin player. This works in the web client only; the apps show the collections (see below).

**Or open it directly:** `https://<your-server>/Youfin/Web/` in a browser where you are signed in to Jellyfin.

For an entry in the Jellyfin sidebar, add this to `jellyfin-web/config.json`:

```json
"menuLinks": [{ "name": "Youfin", "icon": "smart_display", "url": "/Youfin/Web/" }]
```

More details and updates: [docs/INSTALL.md](docs/INSTALL.md).

## FireTV, Swiftfin and Infuse

These apps do not show plugin pages. If you like, Youfin therefore creates **one Jellyfin collection per series** (optionally per channel too), which shows up there like a normal album.

Enable *Create a collection per series* under Dashboard → Plugins → Youfin, then run the scheduled task *Youfin: Sync series collections*. It runs daily afterwards.

## How it works

For the in-Jellyfin view the plugin adds one small script to Jellyfin's web page (`/web/index.html`). It does this only when libraries are chosen and the option is on; otherwise Jellyfin's pages are served untouched. Turn the option off in the plugin settings to remove it.

```
 Jellyfin library ──► Scanner (read-only) ──► In-memory index ◄── state.json (only your assignments)
   titles, images, dates   channel ID, video ID       │
   YouTube Metadata, info.json                        ▼
                                          Web interface  /Youfin/Web/
                                          Collections for FireTV, Swiftfin, Infuse
```

- Video ID from `YoutubeMetadata` or the file name (`[ID]`); channel from the Jellyfin person, the series or the `.info.json`, finally from the folder name.
- Only what Jellyfin does not know is stored: series, assignments, channel aliases, favorites and collections. It lives in `state.json` in the plugin data folder and survives updates.
- The plugin also works without *YouTube Metadata* and without `.info.json`. File and folder names are used instead.

Architecture and decisions: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Languages

The interface is available in **English** and **German**. It picks the language of your browser (or your choice in the header) and falls back to English.
More languages are easy to add: copy one JSON file, translate it and open a pull request. The tests check that translations are complete. See [docs/TRANSLATING.md](docs/TRANSLATING.md).

## Releasing (for maintainers)

Pushing a tag such as `v0.1.1` runs the release workflow: it tests and builds the plugin, publishes the GitHub release with the ZIP and adds the version (with checksum) to `manifest.json`, which is the Jellyfin plugin repository.

## Development

```bash
dotnet test                                          # unit tests
dotnet build -c Release src/Jellyfin.Plugin.Youfin   # build the plugin
```

The plugin is written in C# (.NET 10) against `Jellyfin.Controller` 12.4. The interface is plain JavaScript with no build step (`src/Jellyfin.Plugin.Youfin/Web/app`). Brand assets live once in [`assets/`](assets) and are embedded into the plugin.

## Roadmap

- [x] Channels, search, favorites, playlists
- [x] Series, standalone videos, collections, multi-select
- [x] Collection sync for FireTV, Swiftfin and Infuse
- [x] English and German interface, ready for more languages
- [x] Usable as a Jellyfin plugin repository (manifest and release workflow)
- [ ] Automatic series suggestions (title patterns, playlist fields in `.info.json`)
- [ ] Assign categories manually
- [ ] More than 200 episodes per series in the reorder view

## Credits and notices

- [Jellyfin](https://jellyfin.org) and the [YouTube Metadata](https://github.com/ankenyr/jellyfin-youtube-metadata-plugin) plugin by ankenyr, which this library builds on.
- Logo typeface: [Sora](https://github.com/sora-xor/sora-font) (SIL Open Font License 1.1).
- Youfin is an independent community project and is not affiliated with YouTube, Google or the Jellyfin project. YouTube is a trademark of Google LLC. Youfin does not download videos; it only manages files you already own. Mind the terms of service and copyright when downloading.

## License

[GNU GPL v3](LICENSE) (GPL-3.0-only, the same license as the Jellyfin libraries Youfin builds on) with additional terms under section 7, see [NOTICE](NOTICE):

- keep the copyright notice and the statement that Youfin was **created with AI** (Claude Code by Anthropic) in the source and in the "About" screen of works based on it,
- keep the **reference to this repository**: <https://github.com/brewing8309/Youfin>,
- mark modified versions as different from the original.

Commercial use is allowed under the terms of the GPL (for example, the source code must be provided under the same license).
