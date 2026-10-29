# Betrieb: Home Control auf Handy, Rechner und Server

Stand: Planung. Die App ist als PWA vorbereitet (Manifest, Icons, Service Worker). Der Betrieb über einen
festen Link ist der nächste Schritt und noch **nicht gebaut oder getestet**.

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

## Dafür fehlt noch (nächste Schritte)

1. Start als Dienst (systemd), damit nach einem Absturz oder Neustart alles von allein wieder läuft.
2. Tailscale-HTTPS einrichten (`tailscale serve`), damit die App auf dem iPhone installierbar ist.
3. Agent: API-Schlüssel in n8n eintragen, Basis-Adresse auf den Tailscale-Namen stellen, danach `Auth:RequireLoginForReading` auf `true`.
