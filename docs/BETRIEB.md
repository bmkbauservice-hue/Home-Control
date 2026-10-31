# Betrieb: Home Control auf Handy, Rechner und Server

**Zum Lernen:** [Lernunterlage-Betrieb.pdf](Lernunterlage-Betrieb.pdf)

Stand 31.10.2026: Die App ist als PWA vorbereitet (Manifest, Icons, Service Worker). Für den Dauerbetrieb
gibt es zwei Skripte: `dienst-einrichten.sh` (Dienst) und `tailscale-https-einrichten.sh` (HTTPS-Link).
Beide sind geschrieben, aber auf dem Rechner noch **nicht ausgeführt** worden.

## Ziel

- Ein Link, den man am Rechner als Lesezeichen und am Handy als App-Symbol (Startbildschirm) öffnet.
- Login und Logout (gibt es schon: Anmeldeseite, Abmelden-Knopf in der Navigation).
- Der KI-Agent (n8n auf dem OVH-Server) liest über den API-Schlüssel mit.

## Was schon da ist (PWA)

| Datei | Zweck |
|---|---|
| `frontend/public/manifest.webmanifest` | Name, Farben, Icons, „standalone“ (läuft ohne Browserleiste) |
| `frontend/public/icons/` | App-Symbole (192, 512, maskable, Apple-Touch) |
| `frontend/public/sw.js` | Service Worker: Oberfläche öffnet sich auch bei kurzem Netzausfall; `/api` wird nie zwischengespeichert |
| `frontend/index.html` | Verweise auf Manifest und Apple-Meta-Tags |

Wichtig: Der Service Worker läuft nur im Build (`npm run build`), nicht mit `npm run dev`, und **nur über HTTPS**
(oder `localhost`). Auf dem iPhone geht „Zum Home-Bildschirm“ über Safari → Teilen.

## Betriebsvarianten

| Variante | Vorteil | Nachteil |
|---|---|---|
| A  Backend + Frontend auf dem Linux-PC, Zugriff per **Tailscale** (`tailscale serve` liefert HTTPS) | Sofort möglich, keine Daten wandern, Agent und Handy sind schon im Tailscale-Netz | PC muss laufen |
| B  Alles auf dem **OVH-Server** (Docker, Reverse Proxy mit HTTPS) | Läuft rund um die Uhr, Agent liegt schon dort | Datenbank und Geheimnisse umziehen, Server absichern |

Empfehlung: erst A (ein Link `https://<pc-name>.<tailnet>.ts.net`), später B.

## Ein Link für alles (gebaut, noch nicht getestet)

Das Backend liefert das gebaute Frontend selbst aus, wenn dort eine `index.html` liegt
(`Frontend:Path`, sonst der Ordner `wwwroot`). Dann gibt es einen Port und einen Link, und kein CORS.
Starten im Repo-Hauptordner: `./app-starten.sh` → `http://localhost:5111`.

- Statische Dateien stehen vor der Authentifizierung, damit die Anmeldeseite lädt. Geschützt sind die Daten unter `/api`.
- Unbekannte Adressen unter `/api` bleiben ein echter 404; alle anderen bekommen die `index.html` (React-Router).
- Das Refresh-Cookie ist außerhalb von Development `Secure` (gehört hinter HTTPS). Im Development nicht, deshalb läuft das Skript bewusst
  mit Development: Nur dort werden die User Secrets (Tuya, JWT, Agent) gelesen. Für einen echten Produktivbetrieb auf dem Server
  müssen die Geheimnisse als Umgebungsvariablen kommen (z. B. `Jwt__SigningKey`).

## Dauerbetrieb einrichten (Variante A: Linux-PC + Tailscale)

### 1. Dienst: Home Control läuft von allein

```bash
./dienst-einrichten.sh
```

| Was | Wie |
|---|---|
| Bauen | Frontend (`npm ci`, `npm run build`), Backend als Release (`dotnet publish`) nach `~/.local/share/home-control/app` |
| Dienst | systemd-**Benutzer**dienst `~/.config/systemd/user/home-control.service`, kein `sudo` nötig |
| Adresse | nur `http://127.0.0.1:5200`, also von außen nicht direkt erreichbar |
| Datenbank | dieselbe wie beim Entwickeln: `backend/HomeControl.Api/homecontrol.db` |
| Absturz | `Restart=on-failure`: nach 5 Sekunden startet der Dienst neu |
| Neustart des PCs | `loginctl enable-linger`: Der Dienst startet beim Hochfahren, auch ohne Anmeldung |
| Update | Das Skript einfach noch einmal ausführen (baut neu und startet neu) |

Warum Port **5200**? `dotnet run` beim Entwickeln benutzt 5111. Laufen beide gleichzeitig, kommen sie
sich so nicht in die Quere (kein „address already in use“).

Nützliche Befehle:

```bash
systemctl --user status home-control      # läuft er?
journalctl --user -u home-control -f      # Log live ansehen (Strg+C beendet nur die Anzeige)
systemctl --user restart home-control     # neu starten
systemctl --user stop home-control        # anhalten
systemctl --user disable --now home-control   # ganz abschalten
```

### 2. HTTPS-Link im Tailscale-Netz

Einmalig in der Tailscale-Verwaltung (login.tailscale.com → **DNS**) „MagicDNS“ und
„HTTPS Certificates“ einschalten. Dann:

```bash
./tailscale-https-einrichten.sh
```

Das Skript ruft `tailscale serve --bg http://127.0.0.1:5200` auf. Tailscale holt selbst ein echtes
Zertifikat und leitet `https://<rechner>.<tailnet>.ts.net` an den Dienst weiter. Erreichbar ist das
**nur aus deinem Tailscale-Netz** (iPhone, iPad, MacBook, OVH-Server), nicht aus dem Internet.

Meldet das Skript fehlende Rechte: einmal `sudo tailscale set --operator=$USER`, dann erneut starten.
Abschalten: `tailscale serve reset`.

### 3. iPhone und iPad

1. Tailscale-App installieren und mit demselben Konto anmelden.
2. In **Safari** den Link `https://<rechner>.<tailnet>.ts.net` öffnen und anmelden.
3. Teilen → **„Zum Home-Bildschirm“**. Home Control startet dann wie eine App ohne Browserleiste.

### 4. KI-Agent (n8n)

1. `./agent-schluessel-einrichten.sh`, danach `./dienst-einrichten.sh` (Dienst übernimmt den Schlüssel beim Neustart).
2. In n8n: Basis-Adresse `https://<rechner>.<tailnet>.ts.net`, Header `X-Api-Key` (Zugangsdaten „Header Auth“).
3. Prüfen, dass der Agent Daten bekommt. Neu: `GET /api/status` sagt, welche Geräte offline sind.
4. Erst dann `Auth:RequireLoginForReading` auf `true` setzen und den Dienst neu starten.

## Bewusste Grenzen

- Der Dienst läuft mit `ASPNETCORE_ENVIRONMENT=Development`, weil die Geheimnisse in den User Secrets
  liegen und die nur im Development gelesen werden. Folgen: Die API-Doku (`/scalar`) ist im Tailscale-Netz
  erreichbar, und das Refresh-Cookie ist nicht als `Secure` markiert (über HTTPS wird es trotzdem
  verschlüsselt übertragen). Für einen echten Server (Variante B) gehören die Geheimnisse in
  Umgebungsvariablen (`Jwt__SigningKey`, `Tuya__ClientSecret` …) und die Umgebung auf `Production`.
- Der PC muss laufen. Für 24/7 ist Variante B (OVH-Server, Docker) der nächste Schritt.
