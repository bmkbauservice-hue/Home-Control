#!/usr/bin/env bash
# Richtet Home Control als Dienst ein (systemd, nur für deinen Benutzer, ohne sudo) oder aktualisiert ihn.
# Danach läuft Home Control dauerhaft auf http://127.0.0.1:5200 und startet nach einem Absturz oder
# einem Neustart des Rechners von allein wieder.
#
# Erneut ausführen = aktualisieren: neu bauen, neu veröffentlichen, Dienst neu starten.
# Aufruf im Repo-Hauptordner:  ./dienst-einrichten.sh
#
# Port 5200 statt 5111: So kollidiert der Dienst nicht mit "dotnet run" beim Entwickeln (5111).
# Erklärung: docs/BETRIEB.md und docs/Lernunterlage-Betrieb.pdf
set -euo pipefail
cd "$(dirname "$0")"

REPO="$PWD"
PORT="${HOME_CONTROL_PORT:-5200}"
APP_DIR="$HOME/.local/share/home-control/app"
UNIT_DIR="$HOME/.config/systemd/user"
UNIT="$UNIT_DIR/home-control.service"
# Dieselbe Datenbank wie beim Entwickeln (dotnet run im Backend-Ordner).
DB="$REPO/backend/HomeControl.Api/homecontrol.db"
# Voller Pfad zu dotnet: Ein Dienst kennt den PATH aus dem Terminal nicht.
DOTNET="$(readlink -f "$(command -v dotnet)")"

echo "==> Frontend bauen"
(cd frontend && npm ci --no-audit --no-fund && npm run build)

echo "==> Dienst anhalten (falls er schon läuft), damit die Dateien frei sind"
systemctl --user stop home-control 2>/dev/null || true

echo "==> Backend veröffentlichen nach $APP_DIR"
# Release-Build in einen eigenen Ordner. Änderungen im Repo wirken erst beim nächsten Aufruf
# dieses Skripts. So läuft der Dienst stabil weiter, während du am Code arbeitest.
dotnet publish backend/HomeControl.Api -c Release -o "$APP_DIR" --nologo

echo "==> Oberfläche kopieren nach $APP_DIR/wwwroot"
rm -rf "$APP_DIR/wwwroot"
cp -r frontend/dist "$APP_DIR/wwwroot"

echo "==> Dienst schreiben: $UNIT"
mkdir -p "$UNIT_DIR"
cat > "$UNIT" <<EOF
[Unit]
Description=Home Control (Oberfläche und API)
After=network-online.target

[Service]
# Arbeitsordner = Inhaltsordner der App: Dort liegen appsettings.json und wwwroot.
WorkingDirectory=$APP_DIR
ExecStart=$DOTNET $APP_DIR/HomeControl.Api.dll
Environment=DOTNET_ROOT=$(dirname "$DOTNET")
# Development, weil die Geheimnisse (JWT, Tuya, Blink, Agent) in den User Secrets liegen.
# Die werden nur im Development gelesen. Erreichbar ist der Dienst trotzdem nur lokal und über Tailscale.
Environment=ASPNETCORE_ENVIRONMENT=Development
# Nur auf 127.0.0.1: Von außen kommt man ausschließlich über "tailscale serve" (HTTPS) heran.
Environment=ASPNETCORE_URLS=http://127.0.0.1:$PORT
Environment="ConnectionStrings__DefaultConnection=Data Source=$DB"
# Nach einem Absturz nach 5 Sekunden neu starten.
Restart=on-failure
RestartSec=5

[Install]
WantedBy=default.target
EOF

echo "==> Dienst einschalten und starten"
systemctl --user daemon-reload
systemctl --user enable home-control >/dev/null
systemctl --user restart home-control

# Ohne "Linger" laufen Benutzerdienste nur, solange du angemeldet bist. Mit Linger starten sie
# schon beim Hochfahren des Rechners.
if [ "$(loginctl show-user "$USER" --property=Linger --value 2>/dev/null)" != "yes" ]; then
    echo "==> Dienst auch ohne Anmeldung laufen lassen (Linger)"
    loginctl enable-linger "$USER" 2>/dev/null \
        || echo "Hinweis: Bitte einmal 'sudo loginctl enable-linger $USER' ausführen."
fi

echo "==> Kurz warten und prüfen"
for _ in $(seq 1 20); do
    if curl -fsS -o /dev/null "http://127.0.0.1:$PORT/"; then
        echo "Fertig: Home Control läuft auf http://127.0.0.1:$PORT"
        echo "Status: systemctl --user status home-control    Log: journalctl --user -u home-control -f"
        exit 0
    fi
    sleep 1
done

echo "Der Dienst antwortet nicht. Die letzten Log-Zeilen:"
journalctl --user -u home-control -n 30 --no-pager || true
exit 1
