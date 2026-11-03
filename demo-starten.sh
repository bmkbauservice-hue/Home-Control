#!/usr/bin/env bash
# Startet Home Control im DEMO-MODUS: Beispielhaus, Fake-Tuya, Fake-Blink, keine echten Daten.
# Läuft neben dem Dienst (Port 5200) und neben ./app-starten.sh (Port 5111), ohne sie zu stören:
# eigener Ordner, eigene Datenbank, eigener Port, keine User Secrets. Erklärung: docs/DEMO.md
#
# Aufruf im Repo-Hauptordner:  ./demo-starten.sh             (frisches Beispielhaus, Ereignisse bis heute)
#                              ./demo-starten.sh --behalten  (Demo-Datenbank vom letzten Mal weiterverwenden)
# Beenden mit Strg+C.
set -euo pipefail
cd "$(dirname "$0")"

PORT="${DEMO_PORT:-5300}"
DEMO_DIR="$HOME/.local/share/home-control/demo"
DB="$DEMO_DIR/daten/homecontrol-demo.db"
# Passwort NUR für das Demo-Konto. Es schützt keine echten Daten, deshalb darf es hier stehen.
# Eigenes Passwort: DEMO_PASSWORT=… ./demo-starten.sh (mind. 8 Zeichen, Groß-, Kleinbuchstabe, Ziffer).
DEMO_PASSWORT="${DEMO_PASSWORT:-Demo-Haus-2026}"

echo "==> Frontend bauen"
(cd frontend && npm ci --no-audit --no-fund --silent && npm run build)

echo "==> Backend veröffentlichen nach $DEMO_DIR/app"
# Eigener Ordner wie beim Dienst: Ein späteres Bauen im Repo ändert nichts an der laufenden Demo.
dotnet publish backend/HomeControl.Api -c Release -o "$DEMO_DIR/app" --nologo -v quiet
rm -rf "$DEMO_DIR/app/wwwroot"
cp -r frontend/dist "$DEMO_DIR/app/wwwroot"

mkdir -p "$(dirname "$DB")"
if [[ "${1:-}" != "--behalten" ]]; then
    # Standard: frisch anfangen, damit die Ereignisse der "letzten 7 Tage" wirklich bis heute reichen
    # und jede Vorführung gleich aussieht. Gelöscht wird NUR die Demo-Datenbank.
    rm -f "$DB" "$DB-shm" "$DB-wal"
fi

# Umgebung "Demo": lädt appsettings.Demo.json (DemoMode, Fake-Zugangsdaten), aber KEINE User Secrets.
export ASPNETCORE_ENVIRONMENT=Demo
export ASPNETCORE_URLS="http://localhost:$PORT"
export ConnectionStrings__DefaultConnection="Data Source=$DB"
export HomeControl__DemoPassword="$DEMO_PASSWORT"
# Wegwerf-Schlüssel für die Anmelde-Tokens, bei jedem Start neu. Nicht der echte Schlüssel aus den User Secrets.
Jwt__SigningKey="$(head -c 48 /dev/urandom | base64 -w0)"
export Jwt__SigningKey

echo
echo "==> Demo startet auf http://localhost:$PORT"
echo "    Konto:    demo@example.de"
echo "    Passwort: $DEMO_PASSWORT   (nur für die Demo)"
echo "    Beenden:  Strg+C"
echo
cd "$DEMO_DIR/app"
exec dotnet HomeControl.Api.dll
