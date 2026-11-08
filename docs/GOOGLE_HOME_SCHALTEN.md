# Geräte über Google Home schalten

Stand: 08.11.2026. Diese Ausnahme vom früheren „nur lesend“ hat der Nutzer ausdrücklich für die Wohnzimmer-Stehlampe beauftragt.

**Zum Lernen:** [Lernunterlage-Google-Home-Schalten.pdf](Lernunterlage-Google-Home-Schalten.pdf)

## Warum dieser Weg?

Die Steckdose der Stehlampe liegt in Lidl Home. Das Tuya-Projekt darf sie nicht abfragen (Code 1106). In der offiziellen Google Home-App lässt sie sich schalten. Die Android-Test-App „Muju Home“ nutzt dieselbe Google-Home-Schnittstelle und kann darüber Befehle senden.

## Ablauf

```text
Browser → Backend (Befehl mit Frist bereitlegen)
Muju Home → fragt alle 1,5 s nach → holt den Befehl ab → schaltet über Google Home
Muju Home → liest den neuen Zustand → meldet ihn zurück
Backend → zeigt „Ein“ oder „Aus“ erst jetzt
```

Home Control kann das Handy nicht von sich aus erreichen. Deshalb fragt die App regelmäßig nach (Polling). Der Weg läuft über USB, ADB-Reverse und den vorhandenen Gateway auf Port 5390.

## Regeln

- **Nur Admin** darf schalten, und nur ausdrücklich verknüpfte Geräte.
- **Eindeutige ID:** Verknüpft wird die Geräte-ID aus Google Home. Neu verknüpfen geht nur, wenn Muju Home das Gerät gerade mit Ein/Aus meldet.
- **Absicherung der App:** dieselbe wie bei den Klimasensoren: nur vom eigenen Rechner und nur mit dem Bridge-Schlüssel.
- **Bestätigung:** Der Zustand wird erst gespeichert, wenn das Gerät ihn gemeldet hat.
- **Keine alten Befehle:** Ein Befehl muss innerhalb von 4 Sekunden abgeholt werden und wird genau einmal ausgeliefert. Befehle liegen nur im Arbeitsspeicher. Nach einer Trennung wird nichts nachgeholt.
- **Tuya hat keinen Einfluss:** Hat ein Gerät eine Google-Home-Verknüpfung, überspringt der Tuya-Abgleich es, die Online-LED kommt aus Google Home, und der Tuya-Schalter lehnt ab. Die alte Tuya-ID bleibt gespeichert.
- **PC-Steckdose:** Geräte mit dem Namen „Schlafzimmer“ oder einem PC-Steckdosen-Namen sind gesperrt. Der Schutz erkennt nur den Namen. Geschaltet wird aber ohnehin nur, was ein Admin ausdrücklich verknüpft hat.

## Fehlermeldungen

| Anzeige | Bedeutung |
|---|---|
| Muju Home ist nicht verbunden | Die App hat seit mehr als 6 Sekunden nicht nachgefragt. |
| Muju Home meldet dieses Gerät gerade nicht | Die App läuft, kennt das Gerät aber nicht. |
| Gerät ist laut Google Home offline | Google Home erreicht das Gerät nicht. |
| Nicht rechtzeitig abgeholt | Nichts wurde geschaltet. |
| Keine Rückmeldung, Zustand unbekannt | Befehl wurde abgeholt, die Bestätigung blieb aus. An der Lampe prüfen. |

## Einrichten

Handy per USB verbinden und entsperren. Im Projektordner:

```bash
python3 tools/android-bridge-einrichten.py --install
```

Das Skript sichert den Android-Quellcode, ersetzt die Bridge durch die aktuelle Fassung, installiert die App und startet den Gateway. Das Terminal bleibt offen, und Muju Home bleibt im Vordergrund.

Danach unter **Geräte → Stehlampe → Bearbeiten → Google-Home-Gerät** den Sockel auswählen und speichern.

## Grenzen

Der Weg funktioniert nur, solange das Handy angeschlossen und Muju Home geöffnet ist. Für die Präsentation reicht das, ein Dauerbetrieb ist es nicht.
