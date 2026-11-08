# Blink-Clips: welcher Zugriffsweg funktioniert

Stand: 09.11.2026. Erkundung am echten Konto, ohne Änderung am Projekt-Code. Nichts wurde geschaltet oder gelöscht.

**Zum Lernen:** [Lernunterlage-Blink-Clips.pdf](Lernunterlage-Blink-Clips.pdf)

## Ergebnis

**Der lokale Weg über das Sync Module funktioniert.** Clips vom USB-Stick lassen sich auflisten und als echte MP4 laden.

| Prüfung | Ergebnis am echten Konto |
|---|---|
| Sync Module | online, lokaler Speicher `enabled`, `compatible`, Status `active` |
| Clip-Liste vom Stick | 7 Clips, zuletzt vom 08.11.2026 |
| Ein Clip vom Stick | 304.450 Bytes, MP4 mit H.264 (1920×1080) und AAC, 7,07 Sekunden |
| Abspielbar? | ja: 105 von 106 Videopaketen dekodiert, ein leeres 9-Byte-Paket bei 4,0 Sekunden ohne Bild |
| Cloud-Clips (`media/changed`, 10 Jahre zurück) | Anfrage erfolgreich, 0 Einträge |
| Abo-Angaben im Konto | `subscriptions` und `entitlements` enthalten nur einen Zeitstempel |

**Stick oder tägliches Cloud-Backup?** Der Stick arbeitet als direkter lokaler Speicher. Für ein Cloud-Backup gibt es am Konto keinen Beleg: Die Cloud-Liste ist leer, obwohl auf dem Stick Clips vom 28.10.2026 liegen, und das Konto nennt kein Abo.

## Ablauf

Wege wie im Projekt [blinkpy](https://github.com/fronzbot/blinkpy). Blink hat keine offizielle Schnittstelle, die Wege können sich ändern.

1. `POST …/sync_modules/{id}/local_storage/manifest/request` liefert eine Anfrage-Nummer.
2. Auf den Befehl warten, dann `GET …/manifest/request/{nummer}` lesen: `manifest_id` und `clips` mit `id`, `camera_name`, `created_at`, `size`.
3. `POST …/manifest/{manifest_id}/clip/request/{clip_id}`: Das Sync Module stellt den Clip kurz über Blink bereit.
4. Dieselbe Adresse mit `GET` liefert die MP4-Datei.

Der Weg ändert nichts auf dem Stick. Den Löschbefehl von blinkpy nutzt Home Control nicht.

## Was das für die Alarmseite bedeutet

- Die Liste weckt das Sync Module. Sie sollte zwischengespeichert und nicht bei jedem Seitenaufruf neu angefordert werden.
- `size` ist vermutlich in KiB angegeben: 297 passt zu 304.450 Bytes. Das ist nur an diesem einen Clip belegt.
- Die Clip-Liste enthält **kein Vorschaubild**. Eines entsteht erst aus dem Video selbst.
- Laut blinkpy-Doku gelten lokale Clip-Kennungen nur etwa 10 bis 20 Minuten. Beim Abspielen muss die Liste deshalb notfalls neu geholt werden.
- Ein manueller Import ist nicht nötig, weil der Abruf mit eingestecktem Stick funktioniert.

## Wie geprüft wurde

Der Test lief in einer Wegwerf-Kopie außerhalb des Repos. Sie nutzte dieselbe Sitzung und dasselbe Blink-Token und lief währenddessen anstelle der Testinstanz auf Port 5181, also nie parallel zu ihr. Der Testclip lag nur vorübergehend außerhalb des Repos und wurde nach der Prüfung gelöscht.
