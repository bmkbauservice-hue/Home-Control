# Demo-Modus

**Zum Lernen:** [Lernunterlage-Demo-Modus.pdf](Lernunterlage-Demo-Modus.pdf)

Eine zweite Instanz von Home Control für Präsentationen. Sie sieht aus wie ein echtes Zuhause, enthält aber
**nur erfundene Daten**: Auf dem Beamer erscheinen keine echten Geräte, Kamerabilder oder Namen, und es geht
keine einzige Anfrage an Tuya oder Blink.

## Starten

```bash
./demo-starten.sh
```

Dann im Browser `http://localhost:5300` öffnen und mit `demo@example.de` anmelden. Das Passwort zeigt das
Skript beim Start an (Standard `Demo-Haus-2026`, nur für die Demo). Beenden mit Strg+C.

| Variante | Befehl |
|---|---|
| Frisch (Standard): Beispielhaus neu, Ereignisse der letzten 7 Tage bis heute | `./demo-starten.sh` |
| Demo-Datenbank vom letzten Mal weiterverwenden | `./demo-starten.sh --behalten` |
| Anderer Port | `DEMO_PORT=5301 ./demo-starten.sh` |
| Anderes Passwort | `DEMO_PASSWORT=… ./demo-starten.sh` |

## Was die Demo zeigt

| Bereich | Inhalt |
|---|---|
| Haus | 5 Räume (Wohnzimmer, Küche, Schlafzimmer, Flur, Arbeitszimmer), 12 Geräte: 4 Lampen, 5 Steckdosen mit Strommessung, 1 Türsensor, 2 Temperatursensoren |
| Verlauf | Ereignisse der letzten 7 Tage nach einem Tagesablauf (Licht morgens und abends, Fernseher abends, Kaffeemaschine, Tür 5–9 Mal am Tag, Temperatur viermal am Tag). Die Reports haben so für jeden Tag Werte. |
| Live | Der echte Tuya-Hintergrund-Abgleich läuft jede Minute gegen eine **Fake-Cloud**: Die Watt schwanken, die Kaffeemaschine zieht alle 30 Minuten kurz 1,2 kW, die Leselampe geht ab und zu an, die Haustür ist alle 20 Minuten für 5 Minuten offen. |
| Hinweise | Der Drucker ist ausgesteckt (offline, rote LED), die Haustür hat 15 % Akku („Batterien“ gelb). |
| Kameras | 2 Demo-Kameras (Haustür, Garten) mit selbst gezeichneten Bildern (Aufschrift „DEMO-BILD“). Das Gartenbild ist 2 Tage alt (gelb). „Neues Bild“ funktioniert: kurze Aufnahme, dann die zweite Bildfassung mit Aufnahmezeit „jetzt“. Das Rate Limit (1× pro Minute) gilt auch hier. |
| Banner | „Demo-Modus – Beispieldaten“ auf der Anmeldeseite und über jeder Seite. Die Begrüßung nennt keinen Namen. |

## Wie es funktioniert

| Teil | Echter Betrieb | Demo-Modus |
|---|---|---|
| Umgebung | `Production` (Dienst) bzw. `Development` | `Demo` (lädt `appsettings.Demo.json`) |
| Einstellung | – | `HomeControl:DemoMode = true` |
| Datenbank | `homecontrol.db` | `~/.local/share/home-control/demo/daten/homecontrol-demo.db` |
| Port | 5200 (Dienst), 5111 (Entwicklung) | 5300 |
| Programmdateien | `~/.local/share/home-control/app` | `~/.local/share/home-control/demo/app` |
| Geheimnisse | User Secrets | **keine**; JWT-Schlüssel bei jedem Start zufällig, Tuya-Zugangsdaten erfunden |
| Tuya | `TuyaClient` → Internet | derselbe `TuyaClient` → `DemoTuyaHandler` (Fake-Cloud) |
| Blink | `BlinkHttpCameraClient` | `DemoBlinkCameraClient` (gleiches Interface `IBlinkCameraClient`) |
| Data-Protection-Schlüssel | `…/HomeControl/dataprotection-keys` | `…/HomeControl/dataprotection-keys-demo` |

**Dependency Injection je nach Einstellung** (`Demo/DemoMode.cs`): Program.cs fragt einmal `DemoMode.IsEnabled` und
registriert dann entweder die echten Clients oder die Fakes. Endpunkte, Abgleich, Status-LEDs und Oberfläche
merken keinen Unterschied, denn sie kennen nur das Interface bzw. den `TuyaClient`.

- **Tuya** ist auf der HTTP-Ebene ersetzt (wie `FakeTuyaHandler` in den Tests): Signatur, Token-Cache,
  Hintergrund-Abgleich, Skalierung der Messwerte und Online-Erkennung laufen als echter Code. Die
  Statuscodes sind die der echten Geräte (`switch_1`, `cur_power`, `cur_voltage`, `doorcontact_state`,
  `battery_percentage`, siehe [TUYA.md](TUYA.md)).
- **Blink** ist auf der Interface-Ebene ersetzt (wie `FakeBlinkCameraClient` in den Tests).
- Das Verhalten der Geräte steht als reine Funktion in `Demo/DemoBehavior.cs`: gleiche Uhrzeit, gleiche Werte.
  Die Schwankung kommt aus einer Prüfsumme über Gerät und Minute, nicht aus `Random`. Darum ist es testbar.
- `Demo/DemoHouse.cs` beschreibt Räume und Geräte **einmal**. Seeder und Fake-Cloud lesen beide von dort.

## Sicherheitsregeln

`DemoMode.EnsureConsistent` prüft beim Start, sonst startet die App nicht:

1. `DemoMode = true` nur in der Umgebung `Demo`. Im Dienst (Production) oder beim Entwickeln: Abbruch.
2. Die Umgebung `Demo` nur mit `DemoMode = true`.
3. Im Demo-Modus keine User Secrets (`HomeControl:UseUserSecrets`).
4. Im Demo-Modus muss die Datenbank „demo“ im Dateinamen haben. Die echte `homecontrol.db` bleibt unberührt.
5. Das Demo-Passwort muss gesetzt sein.

Dazu kommt: Die Tuya-Adresse in `appsettings.Demo.json` endet auf `.invalid`, eine Endung, die es nie geben
wird. Selbst ohne Fake käme keine Anfrage bei Tuya an. Lesen verlangt auch in der Demo eine Anmeldung.

Tests: `DemoModeTests` (Start mit Beispielhaus, Banner, Fake-Abgleich, Kameras, alle Abbruchregeln,
`appsettings.json`, `appsettings.Development.json` und `dienst-einrichten.sh` schalten DemoMode nirgends ein)
und `DemoBehaviorTests` (Tür, Watt, Offline-Gerät, Akku, 7 Tage Verlauf, „Neues Bild“).

## Läuft neben dem Dienst

Die Demo hat ihren eigenen Ordner, ihre eigene Datenbank und ihren eigenen Port. Dienst (5200),
`./app-starten.sh` (5111) und Demo (5300) können gleichzeitig laufen. Die Regel „ein Prozess pro Datenbank“
bleibt gewahrt, weil die Demo nie die echte Datenbank öffnet. Ein Prozess pro Blink-Konto auch, denn die
Demo spricht nicht mit Blink.

## Bilder neu erzeugen

Die Kamerabilder sind mit Pillow gezeichnet (keine Fotos, keine Rechte Dritter):

```bash
python3 tools/demo-bilder/bilder_erzeugen.py
```

Sie liegen in `backend/HomeControl.Api/Demo/Bilder` und sind in die DLL eingebettet (`EmbeddedResource`).

## Grenzen

- Die Temperatur kommt nur aus den Beispiel-Ereignissen. Der Abgleich kennt noch keinen Temperatur-Code, weil
  kein echtes Gerät einen liefert (Tuya-Werte nicht raten).
- Was man in der Demo ändert (Gerät anlegen, löschen), ist nach dem nächsten Start ohne `--behalten` weg.
- Der Link „Erstes Konto anlegen“ steht auch in der Demo. Er führt dort nur zu einer Fehlermeldung, weil es
  das Demo-Konto schon gibt (offener Punkt in [UEBERGABE.md](UEBERGABE.md)).
