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

## Dafür fehlt noch (nächste Schritte)

1. Das Backend liefert das gebaute Frontend selbst aus (ein Port, ein Link, kein CORS). Das ist eine kleine Änderung in `Program.cs`.
2. Das Refresh-Cookie (`hc_refresh`) bekommt hinter HTTPS das Flag `Secure`, Prüfung der `SameSite`-Einstellung.
3. Start als Dienst (systemd), damit nach einem Absturz oder Neustart alles von allein wieder läuft.
4. Agent: API-Schlüssel in n8n eintragen, Basis-Adresse auf den Tailscale-Namen stellen, danach `Auth:RequireLoginForReading` auf `true`.
