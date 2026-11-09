# Alarmseite

Stand: 09.11.2026. Nur für Admins sichtbar.

**Zum Lernen:** [Lernunterlage-Alarm.pdf](Lernunterlage-Alarm.pdf)

## Was die Seite zeigt

- **Verlauf:** Türereignisse aus dem Szenarienverlauf mit dem Ergebnis des Scharfschaltens. Ein Ereignis erscheint auch ohne Clip.
- **Clips vom USB-Stick:** Kamera, Aufnahmezeit und „Abspielen“.

## Zeitlich passend

Ein Clip gilt als „zeitlich passend“, wenn er zwischen 30 Sekunden vor und 3 Minuten nach einem Türereignis aufgenommen wurde. Das ist nur eine Aussage über die Zeit. Dass die Tür den Clip ausgelöst hat, ist damit nicht belegt. Ein Clip kann deshalb auch zu zwei Ereignissen passen.

## Wie ein Clip abgespielt wird

1. Die Liste kommt vom Sync Module und wird 5 Minuten zwischengespeichert, weil jede Anfrage das Sync Module weckt.
2. Das Video wird erst beim Klick auf „Abspielen“ geladen.
3. Der Browser spricht nur mit dem Backend, nie direkt mit Blink. Das Backend antwortet mit `no-store`.
4. Bis zum ersten Abspielen zeigt die Karte einen Platzhalter, denn die Blink-Liste enthält kein Vorschaubild. Danach dient ein Standbild aus dem Video als Vorschau. Es bleibt nur in der geöffneten Ansicht.

Auf dem Stick wird nichts verändert oder gelöscht.

## Warum ffmpeg

Blink-Kameras schreiben gelegentlich leere Videopakete ohne Bild in ihre Clips. Chrome brach die Wiedergabe eines echten Clips daran nach etwa 3,7 Sekunden ab. Das Backend verpackt die heruntergeladene Kopie deshalb mit `ffmpeg` neu und lässt solche Pakete weg, ohne Bild oder Ton neu zu kodieren. Die Kopie liegt dafür kurz in einem eigenen temporären Ordner und wird sofort gelöscht.

Fehlt `ffmpeg` oder scheitert die Aufbereitung, liefert das Backend das unveränderte Video. Es kann dann an solchen Stellen abbrechen. Das Docker-Image enthält `ffmpeg`. Der Container-Test prüft, ob es als Anwendungsbenutzer gestartet werden kann.

## Geprüft am echten Clip

Test am 09.11.2026 in Chrome mit einem echten Clip vom USB-Stick:

| Punkt | Ergebnis |
|---|---|
| Laden erst beim Klick | ja, vor dem Klick kein Video-Abruf |
| Wiedergabe | läuft bis zum Ende (7,07 Sekunden, 1920×1080) |
| Ton | Tonspur vorhanden und dekodiert. Hörbar geprüft wurde sie nicht, der Test lief ohne Lautsprecher. |
| Vorspulen | vor und zurück funktioniert |
| Erneut abspielen | ohne neuen Abruf |
| Adressen im Browser | nur die eigene Instanz, keine Blink-Adresse |

Geprüft wurde ein Clip. Die anderen Clips wurden nicht einzeln abgespielt.
