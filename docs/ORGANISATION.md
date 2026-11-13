# Organisation: Kanäle, Serien, Einzelvideos, Sammlungen

Jedes Video ist vollwertiger Teil der Bibliothek, auch ohne Serie oder Kanal-Sammlung.

| Konzept | Bedeutung | Wer ändert |
|---|---|---|
| **Kanal** | Herkunft, automatisch erkannt. Original-Metadaten bleiben unverändert (Umbenennen/Zusammenführen nur als Anzeige). | Admin |
| **Serie** | Geordnete, kuratierte Reihe. Ein Video gehört zu höchstens einer Serie, Serien dürfen Kanäle mischen. Optional "Kanalregel": neue Videos eines Kanals kommen automatisch dazu. | Admin |
| **Sammlung** | Freie Gruppierung pro Nutzer, ein Video kann in mehreren sein, manuell sortierbar. "Später ansehen" ist eine Standard-Sammlung. | jeder Nutzer |
| **Einzelvideo** | Kein Objekt, sondern ein Zustand: Video ohne Serie. Es wird nie automatisch eine Serie erzeugt. | – |

Rangfolge für die Serienzugehörigkeit: "aus Serie gelöst" > ausdrückliche Zuweisung > Kanalregel > Einzelvideo.
Das Video erscheint in "Alle Videos" genau einmal; Serien- und Kanalseiten sind nur Ansichten.

## Datenmodell (`state.json`, Schema 2, nur Abweichungen)
```
channels:  { channelKey: { alias?, mergedInto? } }
series:    { seriesId: { name, sort: published|manual|title, autoChannelKey? } }
overrides: { videoKey: { seriesId? | detached, order? } }
users:     { userId: { favoriteChannels[], collections[{id,name,kind,videoKeys[]}], dismissedSuggestions[] } }
```
`videoKey` = `yt:<YouTube-ID>`, sonst `item:<Jellyfin-ID>` (die YouTube-ID bleibt stabil, wenn die Bibliothek neu angelegt wird).
Zustand je Video und Serienmitgliedschaft werden beim Index-Aufbau berechnet und nicht gespeichert.

## Migration
Beim ersten Start wandelt das Plugin eine Schema-1-Datei automatisch um und legt `state.json.v1.bak` an (wird nie überschrieben).
Aliase und Merges, Kanal-Favoriten und Playlists (jetzt Sammlungen, Reihenfolge bleibt) werden übernommen.
Alle Videos sind danach Einzelvideos; Serien legst du bewusst an (Kanalseite → "Als Serie anlegen").
Eine beschädigte Datei wird als `state.json.corrupt` gesichert.

## Oberfläche
Entdecken: Startseite, Neu hinzugefügt, Alle Videos · Bibliothek: Serien, Einzelvideos, Kanäle, Sammlungen, Kategorien ·
Meins: Weiter ansehen, Später ansehen, Favoriten, Verlauf. Über "⋯" an jedem Video und über den Auswahlmodus
(Mehrfachauswahl) lassen sich Videos Sammlungen und Serien zuordnen oder als Einzelvideo kennzeichnen.

## Apps (FireTV, Swiftfin, Infuse)
Die geplante Aufgabe "Youfin: Serien-Collections synchronisieren" legt pro Serie eine Jellyfin-Collection an (optional auch pro Kanal),
fügt neue Folgen hinzu und entfernt gelöste Videos wieder. Eine Collection gelöschter Serien wird geleert, aber nicht gelöscht.

## Bewusst weggelassen
Automatische Serienvorschläge (Titelmuster, Playlist-Felder der info.json): bei wenigen Kanälen reicht "Als Serie anlegen".
Der Index ist so gebaut, dass eine Vorschlags-Engine später ohne Datenumbau ergänzt werden kann.
