# Installation (Entwicklungsstand)

1. Plugin bauen: `dotnet build -c Release src/Jellyfin.Plugin.Youfin`
2. `Jellyfin.Plugin.Youfin.dll` nach `<Jellyfin-Datenordner>/plugins/Youfin_0.1.0.0/` kopieren, Jellyfin neu starten.
3. Weboberfläche: `https://<server>/Youfin/Web/` (Anmeldung über Jellyfin, gleicher Browser).
4. Link in der Jellyfin-Seitenleiste (für alle Nutzer): in `jellyfin-web/config.json` eintragen:
   `"menuLinks": [{"name": "Youfin", "icon": "smart_display", "url": "/Youfin/Web/"}]`
   (Link öffnet in neuem Tab; Pfad der config.json hängt von der Installation ab.)

# Empfehlung
Videos in einer eigenen Bibliothek (Serien oder Filme) ablegen. Für FireTV/Swiftfin kommen später
Collections pro Kanal (Phase 4).
