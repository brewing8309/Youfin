# Installation

Voraussetzung: Jellyfin 12.4 (.NET 10). Getestet nur mit 12.4.

1. Bauen: `dotnet build -c Release src/Jellyfin.Plugin.Youfin` (oder `.dll` aus dem CI-Artefakt).
2. `Jellyfin.Plugin.Youfin.dll` nach `<Jellyfin-Datenordner>/plugins/Youfin_0.1.0.0/` kopieren und Jellyfin neu starten.
3. Weboberfläche: `https://<server>/Youfin/Web/` (Anmeldung über Jellyfin im selben Browser).
4. Seitenleisten-Link für alle Nutzer in `jellyfin-web/config.json`:
   `"menuLinks": [{"name": "Youfin", "icon": "smart_display", "url": "/Youfin/Web/"}]`
5. FireTV/Swiftfin/Infuse: Dashboard → Plugins → Youfin → "Pro Serie eine Collection anlegen" aktivieren, dann die geplante Aufgabe
   "Youfin: Sync series collections" ausführen (läuft danach täglich). Optional auch Collections pro Kanal.
   Konzept und Datenmodell: [ORGANISATION.md](ORGANISATION.md).

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

## Bibliotheken auswählen
Dashboard → Plugins → Youfin → *Bibliotheken für Youfin*. Wähle die Bibliotheken mit deinen YouTube-Videos; deine übrigen Filme und Serien bleiben dann aus Youfin heraus.
Ohne Auswahl verwendet Youfin alle Bibliotheken (Admins bekommen in der Oberfläche einen Hinweis). Änderungen gelten nach wenigen Sekunden.

## Youfin in Jellyfin (Webclient)
Mit der Option *Youfin-Oberfläche anzeigen, wenn eine dieser Bibliotheken geöffnet wird* (standardmäßig an) zeigt der Jellyfin-Webclient beim Öffnen einer gewählten Bibliothek die Youfin-Oberfläche unter der Jellyfin-Kopfzeile.
*Jellyfin-Ansicht* wechselt zur normalen Bibliotheksansicht, *Youfin-Ansicht öffnen* zurück. Abspielen öffnet den normalen Jellyfin-Player.
Dafür hängt das Plugin ein kleines Skript in `/web/index.html` ein, und zwar nur bei gewählten Bibliotheken und aktiver Option. Mit ausgeschalteter Option bleiben Jellyfins Seiten völlig unverändert.
Die Apps (FireTV, Swiftfin, Infuse) sind nicht betroffen, sie nutzen die Collections.
