# Youfin – Architektur & Plan

## Kontext
Lokale YouTube-Videos (yt-dlp, `[videoId]` im Dateinamen, `.info.json` daneben, Plugin „YouTube Metadata“) sollen in Jellyfin wie ein Offline-YouTube nutzbar sein. Das muss in Web, **FireTV (offizielle App)** und **Swiftfin** funktionieren, Infuse ist nice-to-have. Repo: `brewing8309/Youfin` (privat, leer).

## Analyse-Ergebnisse (verifiziert)
**Jellyfin**
- Stable ist **12.4** (.NET 10, `Jellyfin.Controller`/`Jellyfin.Model` 12.4.0). Das Plugin-Template steht noch auf 10.11, also selbst auf 12.4 anheben.
- Plugin-API: `BasePlugin<T>`, `IHasWebPages`/`PluginPageInfo`, `IPluginServiceRegistrator`, eigene `ControllerBase`-Controller, `ILibraryManager` (Events ItemAdded/Updated/Removed), `IUserDataManager`, `IDtoService`, `ILibraryPostScanTask`, `IScheduledTask`.
- `EnableInMainMenu`-Seiten sehen **nur Admins**. Eine offizielle Nutzer-Seite in jellyfin-web gibt es nicht.

**Clients**
- FireTV (jellyfin-androidtv), Swiftfin und Infuse sind native Apps und **können keine Plugin-Webseiten anzeigen**.
- Swiftfin zeigt: Bibliotheken, Serien, BoxSets (Collections), Favoriten, Resume, Next Up, Latest und Tag-Filter. **Keine Playlists** (PR #2164 offen), keine IChannel-Kanäle, kein Studio-Filter.
- ⇒ Eine YouTube-*Optik* gibt es nur im Web. In den Apps geht nur die **Struktur**, abgebildet über native Jellyfin-Objekte.

**YouTube Metadata** (ankenyr v1.0.3.15, Jellyfin 10.11, nicht für Home Videos)
- Video-ID: `ProviderIds["YoutubeMetadata"]`, sonst Regex `\[([A-Za-z0-9_-]{11})\]` im Dateinamen.
- Schreibt Name, Overview, PremiereDate und das Primary-Bild.
- Kanal landet als **Person (Director)** mit `ProviderIds["YoutubeMetadata"]=channel_id`. Bei TV-Bibliotheken zusätzlich als Series mit channel_id.
- Keine Tags, Kategorien oder Studios. Die stehen nur in `.info.json` (`channel_id`, `channel`, `tags`, `categories`).
- **Risiko:** Die 12.x-Kompatibilität von YouTube Metadata ist offen. Youfin darf sich deshalb nicht darauf verlassen.

## Architektur-Entscheidungen
1. **Zwei Ausgabeschichten, ein Kern**
   - *Native Schicht* (FireTV, Swiftfin, Infuse): Youfin pflegt Jellyfin-**Collections (BoxSets)**: eine pro Kanal, eine pro Kategorie, eine pro Youfin-Playlist. Optional gespiegelt als echte Jellyfin-Playlist für Clients, die Playlists können. Weiter ansehen, Neu, Favoriten und Gesehen liefern die Apps schon nativ.
   - *Web-Schicht*: YouTube-ähnliche SPA, vom Plugin unter `/Youfin/web/` ausgeliefert. Gleiche Origin, nutzt den Jellyfin-Token des Logins. Erreichbar über einen Custom Link im Modern Layout (in Phase 2 verifizieren) und über die Admin-Menüseite. Wiedergabe per Deep-Link auf `/web/#/details?id=…` (nativer Player). Keine inoffizielle Injection.
2. **Keine Metadaten-Duplikate**: Titel, Beschreibung, Bilder, Dauer, Daten, Resume, Gesehen und Video-Favoriten kommen immer live aus der Jellyfin-API und `IUserDataManager`. Youfin schreibt nie in Video-Items.
3. **Eigene Datenhaltung** (SQLite in `Plugin.DataFolderPath`, übersteht Updates)
   - *Ableitbarer Cache:* videoItemId → youtubeId, channelKey, tags, categories (aus info.json, beim Scan neu gebaut).
   - *Nutzerdaten:* Kanal-Aliase/Umbenennungen, Kanal-Merges, Kanal-Favoriten (pro User), Kategorien, Playlists.
4. **Kanal-Erkennung (Priorität)**
   1. channel_id aus der Person „Director“ (`ProviderIds.YoutubeMetadata`)
   2. Series-ProviderId
   3. `.info.json` `channel_id`
   4. Kanalname (uploader/Person/Ordner)
   5. „Unbekannt“

   Danach wird die Merge-Tabelle angewendet. Avatar: Person-Bild → Series-Bild → `*[channelId]*.jpg` → Platzhalter.
5. **Aktualität**: `ILibraryManager`-Events (inkrementell), `ILibraryPostScanTask` (Abgleich) und ein `IScheduledTask` für Rebuild und Collection-Sync.
6. **Robustheit**: Alle Quellen sind optional. Ohne YouTube Metadata greifen info.json und Dateiname. Jede Kette hat Null-Checks, Fehler pro Item werden geloggt (`ILogger<T>`) und der Scan läuft weiter.
7. **Performance**: Index im Speicher plus SQLite. Alle Listen-Endpunkte mit `startIndex`/`limit`. Die SPA lädt per Lazy-Loading/Infinite Scroll, Bilder holt sie direkt über `/Items/{id}/Images`.
8. **Empfehlung Bibliothek**: Am besten für alle Apps ist eine eigene „YouTube“-Bibliothek vom Typ *Serien* (Kanal = Serie). Youfin unterstützt aber alle Typen.

## Projektstruktur (Repo Youfin)
```
src/Jellyfin.Plugin.Youfin/
  Plugin.cs, PluginServiceRegistrator.cs, Configuration/
  Core/        (YouTubeIdParser, InfoJsonReader, ChannelResolver, Index)
  Data/        (SQLite-Repository, Migrations)
  Sync/        (LibraryEventListener, PostScanTask, CollectionSyncTask)
  Api/         (YoufinController: channels, videos, search, playlists, categories, favorites)
  Web/         (SPA: index.html, js, css – eingebettete Ressourcen)
tests/Jellyfin.Plugin.Youfin.Tests/  (xUnit)
docs/ARCHITECTURE.md, docs/INSTALL.md
build.yaml, .github/workflows (Build, Release, manifest.json)
```

## Phasen
- **Phase 2 – Grundgerüst**: Projekt auf 12.4 aufsetzen, Registrierung, Parser und Resolver mit Tests, SQLite, Index, API (Kanäle/Videos paginiert). Spike: Custom Link und Deep-Link-Wiedergabe prüfen.
- **Phase 3 – Web-UI**: Startseite (Neu, Weiter ansehen, Zuletzt angesehen, Meine Kanäle), Grid, Kanalseite, Detailseite, Sortierung, Ansichtsmodi.
- **Phase 4 – Funktionen**: Suche (Titel, Kanal, Beschreibung, ID, Tags), Kanal-Favoriten, Merge, Umbenennen, Playlists, Kategorien, Vor/Zurück im Kanal. Collection-Sync für die Apps.
- **Phase 5 – QS**: Test mit mehreren tausend Videos, mehrere User, Fehlerfälle (ohne YouTube Metadata, ohne info.json), Test auf FireTV, Swiftfin und Infuse. Installations-/Update-Doku, eigenes Plugin-Repository über GitHub Pages.

## Risiken
- Die Apps bekommen kein YouTube-Layout, nur Struktur (technisch nicht änderbar ohne App-Forks).
- Custom Link im Modern Layout und Deep-Link-Autoplay noch unbestätigt; Fallback wäre die Detailseite mit Play-Button.
- YouTube Metadata auf 12.x eventuell defekt; Fallback über info.json ist eingeplant.
- Collections pro Kanal erzeugen zusätzliche Jellyfin-Objekte; per Config abschaltbar.

## Verifikation
- `dotnet test` (Parser, Resolver, Merge, Pagination).
- Test-Jellyfin 12.4 mit Beispielsammlung: Plugin installieren, Scan, API per curl, SPA im Browser (Playwright), Collections in Swiftfin/FireTV sichtbar.
- Keine Container-Neustarts auf dem Homelab ohne Rückfrage.

## Entscheidungen / Verifikation (Stand Phase 3)
- **Persistenz:** JSON-Datei (`state.json`, atomar geschrieben) statt SQLite: wenig Daten (Merges, Aliase, Favoriten), keine zusätzliche Abhängigkeit im Plugin.
- **UI-Auslieferung:** Plugin liefert die SPA als eingebettete Ressourcen unter `/Youfin/Web/` (anonym nur statische Dateien, alle Daten-Endpunkte erfordern Login). Token kommt aus `localStorage.jellyfin_credentials` (gleiche Origin).
- **Verifiziert (Jellyfin 12.4):** Scan, API, Auth (401 ohne Login), Resume/Recent/Favorites über `InternalItemsQuery`, Deep-Link `/web/#/details?id=<id>` öffnet die native Detailseite, `menuLinks` in `jellyfin-web/config.json` (`{name, icon, url}`) zeigt Links in der Seitenleiste für alle Nutzer.
- **Nicht möglich/ungeprüft:** Autoplay per Deep-Link (Detailseite mit Abspielen-Button als Fallback). Playlists, Kategorien, Suche, Kanal-Favoriten: Phase 4.
