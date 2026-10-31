#!/usr/bin/env bash
# Baut das Frontend und startet das Backend, das die Oberfläche gleich mit ausliefert.
# Danach ist alles unter EINER Adresse erreichbar: http://localhost:5111
# Aufruf im Repo-Hauptordner:  ./app-starten.sh
set -euo pipefail
cd "$(dirname "$0")"

echo "==> Frontend bauen"
(cd frontend && npm ci --silent && npm run build)

echo "==> Backend starten (Oberfläche + API auf http://localhost:5111)"
# Development, weil die Geheimnisse (Tuya, JWT, Agent) in den User Secrets liegen.
export ASPNETCORE_ENVIRONMENT="${ASPNETCORE_ENVIRONMENT:-Development}"
export ASPNETCORE_URLS="${ASPNETCORE_URLS:-http://127.0.0.1:5111}"
export Frontend__Path="$PWD/frontend/dist"
# Immer dieselbe Datenbank wie bei "dotnet run" im Backend-Ordner. SQLite legt relative Pfade
# relativ zum aktuellen Ordner an; ohne diese Zeile entstünde im Hauptordner eine neue, leere Datenbank.
export ConnectionStrings__DefaultConnection="${ConnectionStrings__DefaultConnection:-Data Source=$PWD/backend/HomeControl.Api/homecontrol.db}"
exec dotnet run --project backend/HomeControl.Api --no-launch-profile
