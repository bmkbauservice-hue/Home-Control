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

Home Control kann das Handy nicht von sich aus erreichen. Deshalb fragt die App regelmäßig nach (Polling). Die App kann den Gateway per USB und ADB-Reverse oder direkt über eine private Tailscale-HTTPS-Adresse erreichen.

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

### Direkt zu OVH über WLAN

Der OVH-Gateway muss privat über Tailscale erreichbar sein. Auf dem Android-Handy Tailscale im selben
Netz verbinden. Das Handy einmal per USB anschließen und die App mit der privaten Gateway-Adresse
installieren:

```bash
python3 tools/android-bridge-einrichten.py --install --gateway-url 'https://<gateway>.<tailnet>.ts.net:9444'
```

Der Installer prüft zuerst den Gateway mit dem vorhandenen Bridge-Schlüssel. Danach in Muju Home
das Zuhause auswählen und USB abziehen. Auf dem Android-Handy muss der Gateway unter `/healthz`
antworten. Unter **Klima** in Home Control zeigt ein neuer Empfang, dass Daten ankommen. Der
abschließende Test ist Ein/Aus auf dem iPhone über Mobilfunk und Tailscale bei ausgeschaltetem Linux-PC.
Die Android-App und ihre Tailscale-Verbindung müssen dabei zu Hause weiterlaufen.

### USB für Entwicklung und Fehlersuche

Handy per USB verbinden und entsperren. Im Projektordner:

```bash
python3 tools/android-bridge-einrichten.py --install
```

Das Skript sichert den Android-Quellcode, ersetzt die Bridge durch die aktuelle Fassung, installiert die App und startet den Gateway. Das Terminal bleibt offen, und Muju Home bleibt im Vordergrund.

Danach unter **Geräte → Stehlampe → Bearbeiten → Google-Home-Gerät** den Sockel auswählen und speichern.

## USB nach einem Neustart oder Verbindungsabbruch

Ein kurzer USB-Abbruch löscht die Weiterleitung, Muju Home ist dann still getrennt. Das Skript erneuert sie deshalb alle 5 Sekunden.
Ohne Neubau der App reicht:

```bash
python3 tools/android-bridge-einrichten.py --nur-verbinden --device SERIENNUMMER
```

Welche Geräte gerade ankommen, zeigt dieses Werkzeug. Es liest nur:

```bash
python3 tools/geraete-daten-anzeigen.py
```

Fähigkeiten und Akku je Gerät stehen im Handy-Protokoll unter dem Tag `HomeControlBridge`.

## Akkustände

Die App fragt den Matter-Trait `PowerSource` ab. Die Inventur vom 08.11.2026 zeigt: Google Home liefert ihn für keinen Sensor. Temperatursensoren haben daher keine Akkuanzeige. Werte werden nicht erfunden.

Beim Türsensor liefert Tuya immer den letzten gemeldeten Stand. Home Control speichert beim Abgleich mit Tuya, wann der Akku zuletzt gemeldet wurde, und markiert Meldungen ab 3 Tagen als alt.

## Grenzen

Muju Home muss geöffnet bleiben. Im USB-Modus braucht die Bridge den angeschlossenen PC. Im WLAN-Modus braucht sie das Android-Handy, WLAN und Tailscale. Der Hardwaretest bei ausgeschaltetem PC ist Teil der Abnahme.
