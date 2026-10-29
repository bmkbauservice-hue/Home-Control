#!/usr/bin/env bash
# Erzeugt einen zufälligen API-Schlüssel für den KI-Agenten (n8n) und trägt ihn in die
# .NET User Secrets ein (Agent:ApiKey). Der Agent schickt ihn als Header "X-Api-Key".
#
# - Der Schlüssel wird EINMAL angezeigt, damit du ihn in n8n eintragen kannst. Danach steht er
#   nur noch in den User Secrets. Wer ihn kennt, darf alles lesen: nicht weitergeben, nicht in Git.
# - Er geht per stdin an "dotnet user-secrets", nicht als Kommandozeilenargument.
# - Ein vorhandener Schlüssel wird überschrieben. Der Agent braucht dann den neuen.
set -euo pipefail

PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/backend/HomeControl.Api"

KEY="$(python3 -c 'import secrets; print(secrets.token_urlsafe(48))')"

printf '{"Agent:ApiKey": "%s"}' "$KEY" | dotnet user-secrets set --project "$PROJECT_DIR" > /dev/null

echo "Fertig: Agent:ApiKey wurde gespeichert. Bitte das Backend neu starten." >&2
echo "" >&2
echo "Schlüssel für n8n (Header \"X-Api-Key\"), jetzt kopieren, er wird nicht noch einmal gezeigt:" >&2
echo "$KEY"
