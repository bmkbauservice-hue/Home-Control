#!/usr/bin/env bash
# Zeigt die Rohwerte (Statuscodes) aller Geräte, die in Home Control mit einem Tuya-Gerät
# verknüpft sind. Nur lesend: Bei Tuya wird nichts geschaltet, in der Datenbank nichts gespeichert.
#
# - Meldet sich mit deinem Home-Control-Konto an (E-Mail und Passwort werden abgefragt, das
#   Passwort bleibt unsichtbar und steht nie in der Prozessliste) und danach wieder ab.
# - Das Backend muss laufen. Andere Adresse: HC_API=http://100.109.90.93:5111 ./tuya-rohwerte-anzeigen.sh
# - Jeder Aufruf kostet pro Gerät einen Aufruf im Tuya-Kontingent: nicht in einer Schleife starten.
set -euo pipefail

API="${HC_API:-http://localhost:5111}"

read -r -p "E-Mail: " HC_EMAIL
read -r -s -p "Passwort: " HC_PASSWORD
echo
export HC_EMAIL HC_PASSWORD

JAR="$(mktemp)"
trap 'rm -f "$JAR"' EXIT

LOGIN_BODY="$(python3 -c 'import json, os; print(json.dumps({"email": os.environ["HC_EMAIL"], "password": os.environ["HC_PASSWORD"]}))')"
unset HC_PASSWORD

TOKEN="$(printf '%s' "$LOGIN_BODY" \
    | curl -sS -c "$JAR" -X POST "$API/api/auth/login" -H "Content-Type: application/json" -d @- \
    | python3 -c 'import json, sys; print(json.load(sys.stdin)["accessToken"])')" \
    || { echo "Anmeldung fehlgeschlagen. Läuft das Backend, stimmen E-Mail und Passwort?" >&2; exit 1; }

curl -sS -H "Authorization: Bearer $TOKEN" "$API/api/tuya/linked-devices/status" \
    | python3 -c '
import json, sys
data = json.load(sys.stdin)
if "devices" not in data:
    print(json.dumps(data, indent=2, ensure_ascii=False))
    sys.exit(1)
if not data["devices"]:
    print("Kein Gerät ist mit einem Tuya-Gerät verknüpft. Auf der Geräteseite im Feld \"Tuya-Gerät\" verknüpfen.")
for d in data["devices"]:
    print(d["name"] + " (" + d["type"] + ")")
    if d.get("error"):
        print("   FEHLER:", d["error"])
    for s in d.get("status") or []:
        print("   " + s["code"] + " = " + json.dumps(s["value"], ensure_ascii=False))
'

# Abmelden: Refresh-Token beim Server widerrufen.
curl -sS -o /dev/null -b "$JAR" -X POST "$API/api/auth/logout" || true
