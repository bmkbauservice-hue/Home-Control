# Welche Geräte Home Control wirklich erreicht

Stand: 08.11.2026. Alle Angaben stammen aus lesenden Prüfungen. Es wurde dabei kein Gerät geschaltet.
Google-Home-Spalte: Inventur von Muju Home vom selben Tag.
Geräte-IDs, IP-Adressen und lokale Schlüssel stehen bewusst nicht in dieser Datei.

**Zum Lernen:** [Lernunterlage-Geraete-Erreichbarkeit.pdf](Lernunterlage-Geraete-Erreichbarkeit.pdf)

## Vier verschiedene Gründe, warum etwas nicht geht

| Befund | Bedeutung | Beispiel |
|---|---|---|
| Fehlende Berechtigung | Das Gerät kann aktiv sein, aber das Tuya-Projekt darf es nicht abfragen. | Tuya-Code 1106, Gerät fehlt in der App-Liste |
| Offline | Das Tuya-Projekt kennt das Gerät, Tuya erreicht es gerade nicht. | Barlicht |
| Fehlende Schaltfunktion | Das Gerät ist erreichbar, meldet aber kein boolesches `switch_1`. | Türsensor |
| Fehlender Messwert | Das Gerät lässt sich schalten, meldet aber keine Leistung. | Heizdecke |

Home Control nennt beim Schalten jetzt genau die zutreffende Ursache und sendet in diesen Fällen keinen Befehl.

## Stand je Gerät

| Gerät | Tuya-Projekt | Im WLAN gehört | Google Home / Muju Home | Schalten | Strommessung | Woran es liegt |
|---|---|---|---|---|---|---|
| Heizdecke | online | ja, Protokoll 3.3 | nicht nötig | **am Gerät bestätigt** | nein, meldet nur `switch_1` und Countdown | nichts offen |
| Stehlampe | gespeicherte ID und Lidl-ID fehlen, jeweils 1106 | ja, unter der Lidl-ID, Protokoll 3.4 | online, Ein/Aus, verknüpft | Weg gebaut, am Gerät noch nicht bestätigt | nein, nur `OnOff` | Tuya: fehlende Berechtigung. Weiter über Google Home: [GOOGLE_HOME_SCHALTEN.md](GOOGLE_HOME_SCHALTEN.md) |
| Bad Licht (Eintrag „Bad Steckdose“ am 08.11. gelöscht) | „Bad Licht“, offline, letzte Meldung 06.11. | keinem gehörten Gerät zugeordnet | offline, Ein/Aus | offline | nein, kein `cur_power` | offline in Tuya und Google Home |
| Barlicht | offline, letzte Meldung 08.11. | nein | offline, Ein/Aus | nicht getestet, `switch_1` vorhanden | `cur_power` vorhanden, kein aktueller Wert | offline in Tuya und Google Home. Der gespeicherte 0-Watt-Wert belegt keine aktuelle Messung |
| Küche Licht | nicht im Projekt | unbestätigt | online, Ein/Aus, Helligkeit, Farbe, verknüpft | Weg gebaut, am Gerät noch nicht bestätigt | nein | nichts offen außer dem Test am Gerät |
| PC Moonlight (Eintrag am 08.11. gelöscht) | nicht im Projekt | unbestätigt | zwei Einträge „LSC Moodlight“, einer online, einer offline | nicht zugeordnet | nein | welcher Eintrag welches Licht ist, ist unbestätigt |
| PC-Steckdose (LSC „Schlafzimmer“) | fehlt, Direktabruf 40001900 | unbestätigt | „Schlafzimmer“, offline, nur `OnOff`, **gesperrt** | **bewusst nie** | gewünscht, Google Home liefert aber keine Energiewerte | fehlende Berechtigung und fehlender `local_key` |
| Muju Gateway | Untergeräte-Abruf 1106 | unbestätigt | nicht zutreffend | nicht zutreffend | nicht zutreffend | fehlende Berechtigung, Gatewaymodell ungeklärt |
| Türsensor | online gemeldet, letzte Meldung 02.11. | nein | online, Kontakt ohne Akku | keine Schaltfunktion | nein, batteriebetrieben | Tuya hat seit dem 02.11. keine neue Meldung. Die 90 % sind dieser alte Stand |
| Vier Klimasensoren | nicht im Projekt | nein | online, Temperatur und Luftfeuchte, **kein Akku** | nicht zutreffend | nein, batteriebetrieben | Google Home liefert keinen Akkustand, und in Tuya liegen sie nicht |

„Im WLAN gehört“ belegt nur, dass das Gerät im Heimnetz aktiv ist. Lesen oder Schalten ist damit noch nicht möglich.

## Was der Stehlampe fehlt

**Tuya-Cloud:** Das verknüpfte Projekt sieht nur Geräte des verknüpften App-Kontos. Tuya nennt für 1106 unter
anderem Geräte, die nicht aus einer App des eigenen Entwicklerkontos stammen. Die Steckdose liegt in Lidl Home.

**Lokal im WLAN:** Die Steckdose meldet sich mit Protokoll 3.4. Für Lesen und Schalten braucht man ihren
gerätespezifischen `local_key`. Der ist nicht derselbe Schlüssel wie für die öffentlichen Rundrufe.

**Regulärer Weg zum Schlüssel:** Tuya gibt den `local_key` über „Query Device Details“ nur für Geräte heraus, die
das eigene Cloud-Projekt sehen darf. Das setzt voraus, dass das Gerät in einem mit dem Projekt verknüpften Konto
von Tuya Smart oder Smart Life liegt. Dafür müsste die Steckdose neu gekoppelt werden und würde dann voraussichtlich
aus Lidl Home verschwinden. Das ist derzeit ausdrücklich nicht gewünscht. Eine dokumentierte Verknüpfung von Lidl Home
mit einem eigenen Tuya-Projekt wurde nicht gefunden.

**Google Home:** Die Android-Test-App enthält den Ein/Aus-Trait `OnOff`: Sie liest `onOff` und ruft `on()` oder `off()` auf.
Der Rückweg für Schaltbefehle ist gebaut. Ob Muju Home die Steckdose mit Ein/Aus anbietet, zeigt erst der Test am Handy.

## Strommessung

- Die Übersicht trennt Watt und kWh. kWh sind als geschätzt gekennzeichnet.
- Nur Licht, Steckdosen und Schalter zählen mit. Batteriesensoren sind ausgeschlossen.
- Ein Wattwert zählt nur, wenn das Gerät online ist und die Probe höchstens 30 Minuten alt ist.
- Geräte ohne Messung erscheinen unter „Geräte und Messstatus“ mit ihrem Grund, ohne erfundenen Wert.
- Die Android-Test-App enthält keinen Energie-Trait. Der PC-Messpunkt lässt sich über diesen Weg derzeit nicht auslesen.

## Selbst prüfen

Beide Werkzeuge lesen nur und schalten nichts:

```bash
python3 tools/tuya-lan-lauschen.py
```

```bash
python3 tools/tuya-lan-lauschen.py --zuordnung | dotnet run --project tools/TuyaDiagnose -- --abgleich
```

Solange die Testinstanz auf Port 5181 läuft, das Werkzeug nur starten, wenn keine Backend-Änderung offen ist.
`dotnet run` baut sonst auch das Backend neu und überschreibt dessen laufende Dateien.

## Quellen

- [Tuya: Ursachen für Fehler 1106](https://support.tuya.com/en/help/_detail/K9kui9wvuwc93)
- [LocalTuya: Cloud-Einrichtung und Fehler 1106](https://xzetsubou.github.io/hass-localtuya/cloud_api)
- [Google: Device API](https://developers.home.google.com/apis/android/device)
