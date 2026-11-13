# Installation

Voraussetzung: Jellyfin 12.4 (.NET 10). Getestet nur mit 12.4.

1. Bauen: `dotnet build -c Release src/Jellyfin.Plugin.Youfin` (oder `.dll` aus dem CI-Artefakt).
2. `Jellyfin.Plugin.Youfin.dll` nach `<Jellyfin-Datenordner>/plugins/Youfin_0.1.0.0/` kopieren und Jellyfin neu starten.
3. Weboberfläche: `https://<server>/Youfin/Web/` (Anmeldung über Jellyfin im selben Browser).
4. Seitenleisten-Link für alle Nutzer in `jellyfin-web/config.json`:
   `"menuLinks": [{"name": "Youfin", "icon": "smart_display", "url": "/Youfin/Web/"}]`
5. FireTV/Swiftfin/Infuse: Dashboard → Plugins → Youfin → "Pro Serie eine Collection anlegen" aktivieren, dann die geplante Aufgabe
   "Youfin: Serien-Collections synchronisieren" ausführen (läuft danach täglich). Optional auch Collections pro Kanal.
   Konzept und Datenmodell: `docs/ORGANISATION.md`.

## Update
Neue DLL ersetzen und Jellyfin neu starten. Einstellungen und Nutzerdaten (`<Plugin-Datenordner>/state.json`) bleiben erhalten. Eine ältere `state.json` (Schema 1) wird automatisch migriert, die Sicherung heißt `state.json.v1.bak`.

## Daten
- Videodaten kommen live aus Jellyfin (nichts wird kopiert oder überschrieben).
- Kanalnamen: Jellyfin-Person (Director) → Serie → `.info.json` → Ordnername.
- Youfin speichert nur Kanal-Aliase und -Merges, Serien, Zuordnungen, Kanal-Favoriten und Sammlungen in `state.json`.

## Grenzen
- Die YouTube-Optik gibt es nur im Web. Die Apps zeigen Collections.
- Autoplay per Link ist nicht möglich: Die Oberfläche öffnet die Jellyfin-Detailseite des Videos.
- Serien, Merges und Zuordnungen werden ohne Neuscan der Bibliothek angewendet. Serien und Kanal-Verwaltung können nur Admins ändern, Sammlungen jeder Nutzer für sich.
