# Installation

Voraussetzung: Jellyfin 12.4 (.NET 10). Getestet nur mit 12.4.

1. Bauen: `dotnet build -c Release src/Jellyfin.Plugin.Youfin` (oder `.dll` aus dem CI-Artefakt).
2. `Jellyfin.Plugin.Youfin.dll` nach `<Jellyfin-Datenordner>/plugins/Youfin_0.1.0.0/` kopieren und Jellyfin neu starten.
3. Weboberfläche: `https://<server>/Youfin/Web/` (Anmeldung über Jellyfin im selben Browser).
4. Seitenleisten-Link für alle Nutzer in `jellyfin-web/config.json`:
   `"menuLinks": [{"name": "Youfin", "icon": "smart_display", "url": "/Youfin/Web/"}]`
5. FireTV/Swiftfin/Infuse: Dashboard → Plugins → Youfin → "Collection pro Kanal" aktivieren, dann die geplante Aufgabe
   "Youfin: Kanal-Collections synchronisieren" ausführen. Die Kanäle erscheinen dort als Collections.

## Update
Neue DLL ersetzen und Jellyfin neu starten. Einstellungen und Nutzerdaten (`<Plugin-Datenordner>/state.json`) bleiben erhalten.

## Daten
- Videodaten kommen live aus Jellyfin (nichts wird kopiert oder überschrieben).
- Kanalnamen: Jellyfin-Person (Director) → Serie → `.info.json` → Ordnername.
- Youfin speichert nur Kanal-Aliase, Merges, Favoriten und Playlists in `state.json`.

## Grenzen
- Die YouTube-Optik gibt es nur im Web. Die Apps zeigen Collections.
- Autoplay per Link ist nicht möglich: Die Oberfläche öffnet die Jellyfin-Detailseite des Videos.
- Änderungen an Merges/Aliasen lösen einen kompletten Neuaufbau des Index aus (bei sehr großen Sammlungen einige Sekunden).
