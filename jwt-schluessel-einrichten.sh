#!/usr/bin/env bash
# Erzeugt einen zufälligen JWT-Signaturschlüssel und trägt ihn in die .NET User Secrets ein.
#
# - Der Schlüssel wird nie angezeigt. Er geht per stdin an "dotnet user-secrets", nicht als
#   Kommandozeilenargument (sonst wäre er kurz in der Prozessliste sichtbar).
# - Nichts davon landet im Projektordner oder in Git.
# - Ein vorhandener Schlüssel wird überschrieben. Danach sind alle ausgestellten Access Tokens
#   ungültig, das Frontend holt sich per Refresh automatisch neue (Refresh-Tokens bleiben gültig).
set -euo pipefail

PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/backend/HomeControl.Api"

# 64 Zufallsbytes als Base64Url-Text (86 Zeichen), aus dem kryptografischen Zufallsgenerator.
python3 -c 'import json, secrets; print(json.dumps({"Jwt:SigningKey": secrets.token_urlsafe(64)}))' \
    | dotnet user-secrets set --project "$PROJECT_DIR" > /dev/null

echo "Fertig: Jwt:SigningKey wurde gespeichert. Bitte das Backend neu starten." >&2
