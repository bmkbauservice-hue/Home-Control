#!/usr/bin/env bash
# Macht Home Control in deinem Tailscale-Netz über HTTPS erreichbar:
#   https://<rechnername>.<tailnet>.ts.net   →   http://127.0.0.1:5200 (der Dienst)
# Nur Geräte in deinem Tailscale-Netz (iPhone, iPad, MacBook, OVH-Server) kommen heran, nicht das Internet.
#
# Voraussetzungen:
#   1. Der Dienst läuft (./dienst-einrichten.sh).
#   2. In der Tailscale-Verwaltung (login.tailscale.com → DNS) sind "MagicDNS" und
#      "HTTPS Certificates" eingeschaltet.
#
# Aufruf im Repo-Hauptordner:  ./tailscale-https-einrichten.sh
# Wieder abschalten:           tailscale serve reset
set -euo pipefail

PORT="${HOME_CONTROL_PORT:-5200}"

if ! command -v tailscale >/dev/null; then
    echo "tailscale ist auf diesem Rechner nicht installiert." >&2
    exit 1
fi

if ! curl -fsS -o /dev/null "http://127.0.0.1:$PORT/"; then
    echo "Auf Port $PORT antwortet nichts. Zuerst ./dienst-einrichten.sh ausführen." >&2
    exit 1
fi

# --bg: bleibt dauerhaft eingerichtet, auch nach einem Neustart von Tailscale.
# Tailscale holt dafür selbst ein echtes HTTPS-Zertifikat für den .ts.net-Namen.
if ! tailscale serve --bg "http://127.0.0.1:$PORT"; then
    echo >&2
    echo "Das hat nicht geklappt. Meist fehlt die Erlaubnis. Einmalig ausführen:" >&2
    echo "  sudo tailscale set --operator=$USER" >&2
    echo "und dieses Skript danach noch einmal starten." >&2
    exit 1
fi

NAME="$(tailscale status --json | python3 -c 'import json, sys; print(json.load(sys.stdin)["Self"]["DNSName"].rstrip("."))')"
echo
echo "Fertig. Home Control im Tailscale-Netz:  https://$NAME"
echo "iPhone/iPad: Tailscale-App an, Link in Safari öffnen, Teilen → „Zum Home-Bildschirm“."
echo "KI-Agent (n8n): Basis-Adresse https://$NAME, Header X-Api-Key wie gehabt."
echo
tailscale serve status
