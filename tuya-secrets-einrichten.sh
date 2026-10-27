#!/usr/bin/env bash
# Trägt die Tuya-Zugangsdaten interaktiv in die .NET User Secrets des Backends ein.
#
# - Eingaben werden verdeckt gelesen und nie ausgegeben.
# - Leere Eingaben werden abgelehnt.
# - Die Werte gehen per stdin an "dotnet user-secrets", nicht als Kommandozeilenargument
#   (sonst wären sie kurz in der Prozessliste sichtbar).
# - Nichts davon landet im Projektordner oder in Git.
set -euo pipefail

PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/backend/HomeControl.Api"

if [[ ! -t 0 ]]; then
    echo "Bitte direkt im Terminal ausführen (keine umgeleitete Eingabe)." >&2
    exit 1
fi

# Liest einen Wert verdeckt ein, entfernt Leerzeichen am Rand und fragt bei leerer Eingabe erneut.
read_secret() {
    local label="$1" value
    while true; do
        read -r -s -p "$label: " value
        echo >&2
        value="${value#"${value%%[![:space:]]*}"}"
        value="${value%"${value##*[![:space:]]}"}"
        if [[ -n "$value" ]]; then
            printf '%s' "$value"
            return
        fi
        echo "Leere Eingabe ist nicht erlaubt, bitte erneut eingeben." >&2
    done
}

echo "Tuya-Zugangsdaten eintragen (Eingabe bleibt unsichtbar)." >&2
client_id="$(read_secret "Client-ID (Access ID)")"
client_secret="$(read_secret "Client-Secret (Access Secret)")"
test_device_id="$(read_secret "TestDeviceId")"

# JSON sicher erzeugen (Sonderzeichen werden korrekt maskiert) und an user-secrets übergeben.
TUYA_CLIENT_ID="$client_id" TUYA_CLIENT_SECRET="$client_secret" TUYA_TEST_DEVICE_ID="$test_device_id" \
    python3 -c 'import json, os; print(json.dumps({
        "Tuya:ClientId": os.environ["TUYA_CLIENT_ID"],
        "Tuya:ClientSecret": os.environ["TUYA_CLIENT_SECRET"],
        "Tuya:TestDeviceId": os.environ["TUYA_TEST_DEVICE_ID"],
    }))' \
    | dotnet user-secrets set --project "$PROJECT_DIR" > /dev/null

unset client_id client_secret test_device_id

echo "Fertig: Tuya:ClientId, Tuya:ClientSecret und Tuya:TestDeviceId wurden gespeichert." >&2
