# Organisation: channels, series, standalone videos, collections

Every video is a full part of the library, even without a series or channel collection.

| Concept | Meaning | Who changes it |
|---|---|---|
| **Channel** | Origin, detected automatically. The original metadata stays untouched (rename/merge only change the display). | admin |
| **Series** | An ordered, curated series. A video belongs to at most one series; series may mix channels. Optional *channel rule*: new videos of a channel join automatically. | admin |
| **Collection** | Free grouping per user. A video can be in several, sortable by hand. *Watch later* is a built-in collection. | each user |
| **Standalone video** | Not an object but a state: a video without a series. A series is never created automatically. | – |

Precedence for series membership: "removed from series" > explicit assignment > channel rule > standalone.
A video appears exactly once in "All videos"; series and channel pages are only views.

## Data model (`state.json`, schema 2, deviations only)
```
channels:  { channelKey: { alias?, mergedInto? } }
series:    { seriesId: { name, sort: published|manual|title, autoChannelKey? } }
overrides: { videoKey: { seriesId? | detached, order? } }
users:     { userId: { favoriteChannels[], collections[{id,name,kind,videoKeys[]}], dismissedSuggestions[] } }
```
`videoKey` = `yt:<YouTube ID>`, otherwise `item:<Jellyfin ID>` (the YouTube ID stays stable if the library is re-added).
A video's state and series membership are computed when the index is built and are not stored.

## Migration
On first start the plugin converts a schema 1 file automatically and writes `state.json.v1.bak` (never overwritten).
Aliases, merges, channel favorites and playlists (now collections, order kept) are carried over.
Afterwards all videos are standalone videos; you create series deliberately (channel page → *Create series*).
A damaged file is saved as `state.json.corrupt`.

## Interface
Discover: Home, Recently added, All videos · Library: Series, Standalone videos, Channels, Collections, Categories ·
Mine: Continue watching, Watch later, Favorites, History. The ⋯ menu on every video and the multi-select mode assign videos
to collections and series or mark them as standalone.

## Apps (FireTV, Swiftfin, Infuse)
The scheduled task *Youfin: Sync series collections* creates one Jellyfin collection per series (optionally per channel too),
adds new episodes and removes videos taken out of a series. The collection of a deleted series is emptied but not deleted.

## Left out on purpose
Automatic series suggestions (title patterns, playlist fields of the info.json): with few channels, *Create series* is enough.
The index is built so that a suggestion engine can be added later without changing the data.
