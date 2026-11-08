# Szenarien

Stand: 08.11.2026. Erste Regel: „Wenn die Flurtür von Zu auf Auf wechselt, schalte ein Blink-System scharf.“

**Zum Lernen:** [Lernunterlage-Szenarien.pdf](Lernunterlage-Szenarien.pdf)

Scharfschalten ist auf ausdrücklichen Nutzerauftrag vom 08.11.2026 eine weitere Ausnahme vom früheren „nur lesend“.

## Regeln

- Nur Admins sehen und ändern Regeln. Neue Regeln starten immer ausgeschaltet.
- Die Regel läuft im Backend, also auch bei geschlossenem Browser.
- „Einmal testen“ schaltet das System wirklich scharf. Entschärft wird in der Blink-App.
- Der Verlauf nennt je Lauf den Auslöser und das Ergebnis: Bestätigt, Nicht bestätigt oder Fehler.
- Bestätigt ist ein Lauf erst, wenn Blink das System nach dem Befehl beim erneuten Lesen als scharf meldet.

## Was als frisches Türsignal zählt

Das Signal kommt über Google Home und Muju Home, weil Port 5181 Tuya nicht automatisch abfragt und Tuya selbst nur alle paar Minuten gelesen würde.

| Fall | Ergebnis |
|---|---|
| Die App meldet erst Zu, bei der nächsten Nachfrage Auf | löst aus |
| Dieselbe Meldung Auf noch einmal | nichts |
| Tür ist beim Start der App oder des Backends schon offen | nichts |
| Auf nach einer Unterbrechung der Verbindung (über 6 Sekunden) | nichts |
| Tür meldet offline | nichts |
| Erneuter Wechsel innerhalb von 60 Sekunden | nichts |
| Auslöser wartet länger als 2 Minuten | verworfen |

Mit Google-Home-Verknüpfung schreibt Tuya den Türzustand nicht mehr. Tuya liefert weiter nur den Akkustand.

## Grenzen

- Die Regel funktioniert nur, solange Handy und Muju Home verbunden sind.
- Bisher gibt es genau einen Auslöser (Tür geht auf) und eine Aktion (Blink scharfschalten). Weitere Bedingungen, mehrere Aktionen und der Wecker folgen.

## Livebild

Blink liefert Livebilder über das eigene Protokoll `immis` als MPEG-TS. Unter **Blink → Livebild prüfen** fordert Home Control für wenige Sekunden einen Strom an und zählt echte Videopakete. Gespeichert wird nichts. Das Etikett „Live“ gibt es erst, wenn eine Wiedergabe eingebaut ist und wirklich Video läuft.
