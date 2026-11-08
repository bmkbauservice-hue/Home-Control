# Welche Geräte Home Control wirklich erreicht

Stand: 08.11.2026. Alle Angaben stammen aus lesenden Prüfungen. Es wurde dabei kein Gerät geschaltet.
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
| Stehlampe | gespeicherte ID und Lidl-ID fehlen, jeweils 1106 | ja, unter der Lidl-ID, Protokoll 3.4 | offizielle Google Home-App schaltet sie, Muju Home noch nicht geprüft | Weg über Muju Home gebaut, am Gerät noch nicht bestätigt | unbekannt | Tuya: fehlende Berechtigung und fehlender `local_key`. Weiter über Google Home: [GOOGLE_HOME_SCHALTEN.md](GOOGLE_HOME_SCHALTEN.md) |
| Bad Steckdose | „Bad Licht“, offline, letzte Meldung 06.11. | keinem gehörten Gerät zugeordnet | am Handy noch nicht geprüft | offline | nein, kein `cur_power` | offline. Ob der Tuya-Eintrag noch das aktuelle Lidl-Gerät ist, ist unbestätigt |
| Barlicht | offline, letzte Meldung 08.11. | nein | am Handy noch nicht geprüft | nicht getestet, `switch_1` vorhanden | `cur_power` vorhanden, kein aktueller Wert | offline. Der gespeicherte 0-Watt-Wert belegt keine aktuelle Messung |
| Küche Licht | nicht im Projekt, keine Zuordnung | unbestätigt | am Handy noch nicht geprüft | nein | unbekannt | Funkweg in Lidl Home noch unbestätigt |
| PC Moonlight | nicht im Projekt, keine Zuordnung | unbestätigt | am Handy noch nicht geprüft | nein | unbekannt | Funkweg in Lidl Home noch unbestätigt |
| PC-Steckdose (LSC „Schlafzimmer“) | fehlt, Direktabruf 40001900 | unbestätigt | **nicht ansteuern** | **bewusst nie** | gewünscht, nur lesend | fehlende Berechtigung und fehlender `local_key` |
| Muju Gateway | Untergeräte-Abruf 1106 | unbestätigt | nicht zutreffend | nicht zutreffend | nicht zutreffend | fehlende Berechtigung, Gatewaymodell ungeklärt |
| Türsensor | online | nein | nicht nötig | keine Schaltfunktion | nein, batteriebetrieben | nichts offen |
| Vier Klimasensoren | nicht im Projekt | nein | angebunden, frische Werte nur mit laufender App | nicht zutreffend | nein, batteriebetrieben | Handy und Gateway müssen laufen |

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
