#!/usr/bin/env bash
# Prüft den ganzen Stand auf einmal:
#   Backend bauen → offene Modelländerungen (fehlende Migration?) → Tests → Frontend bauen → Lint
# Jeder Schritt läuft, auch wenn ein früherer fehlschlägt. Am Ende steht eine Zusammenfassung.
# Die komplette Ausgabe landet zusätzlich in pruefen.log (steht in .gitignore, kommt nicht ins Repo).
#
# Aufruf im Repo-Hauptordner:  ./pruefen.sh
set -uo pipefail
cd "$(dirname "$0")"

LOG="$PWD/pruefen.log"
: > "$LOG"
ergebnisse=()
fehler=0

# Führt einen Schritt aus, zeigt die Ausgabe an und schreibt sie ins Log.
schritt() {
    local name="$1"
    shift
    printf '\n===== %s =====\n' "$name" | tee -a "$LOG"
    "$@" 2>&1 | tee -a "$LOG"
    local status=${PIPESTATUS[0]}
    if [ "$status" -eq 0 ]; then
        ergebnisse+=("OK      $name")
    else
        ergebnisse+=("FEHLER  $name (Exit-Code $status)")
        fehler=1
    fi
}

{
    echo "Prüfung vom $(date '+%d.%m.%Y %H:%M:%S')"
    echo "Branch: $(git rev-parse --abbrev-ref HEAD)   Commit: $(git log -1 --format='%h %s')"
    echo "Nicht committete Änderungen:"
    git status --short
    echo ".NET SDK: $(dotnet --version 2>/dev/null || echo fehlt)   Node: $(node --version 2>/dev/null || echo fehlt)   npm: $(npm --version 2>/dev/null || echo fehlt)"
} 2>&1 | tee -a "$LOG"

schritt "dotnet tool restore" dotnet tool restore
schritt "dotnet build" dotnet build --nologo
# Meldet einen Fehler, wenn sich das Datenmodell geändert hat, es aber noch keine Migration dafür gibt.
schritt "Migrationen vollständig" dotnet ef migrations has-pending-model-changes --project backend/HomeControl.Api --no-build
schritt "dotnet test" dotnet test --nologo --no-build
schritt "npm ci" bash -c "cd frontend && npm ci --no-audit --no-fund"
schritt "npm run build" bash -c "cd frontend && npm run build"
schritt "npm run lint" bash -c "cd frontend && npm run lint"

{
    printf '\n===== Zusammenfassung =====\n'
    printf '%s\n' "${ergebnisse[@]}"
    if [ "$fehler" -eq 0 ]; then
        echo "Alles grün."
    else
        echo "Mindestens ein Schritt ist fehlgeschlagen. Details stehen weiter oben und in pruefen.log."
    fi
} 2>&1 | tee -a "$LOG"

exit "$fehler"
