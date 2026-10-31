#!/usr/bin/env bash
# Vorbereitung für die Umstellung auf ASP.NET Core Identity (Branch feature/identity).
# - Sichert deine Datenbank(en) (Kopie mit Endung .vor-identity.bak).
# - Löscht danach NUR die alten Benutzer und Refresh-Tokens. Räume, Geräte, Ereignisse und
#   Blink-Zugangsdaten bleiben unangetastet. Danach legst du dein Konto neu an
#   ("Erstes Konto anlegen", du wirst wieder Admin).
# Aufruf im Repo-Hauptordner:  ./identity-vorbereiten.sh
# Mehr dazu: docs/IDENTITY.md
set -euo pipefail
cd "$(dirname "$0")"

found=0
for db in homecontrol.db backend/HomeControl.Api/homecontrol.db; do
  [ -f "$db" ] || continue
  found=1
  backup="$db.vor-identity.bak"
  if [ -e "$backup" ]; then echo "Sicherung gibt es schon: $backup"; else cp "$db" "$backup" && echo "Gesichert: $backup"; fi
  python3 - "$db" <<'PY'
import sqlite3, sys
con = sqlite3.connect(sys.argv[1])
tables = {r[0] for r in con.execute("select name from sqlite_master where type='table'")}
if "AspNetUsers" in tables:
    print(f"{sys.argv[1]}: Identity-Tabellen sind schon da, nichts zu tun.")
elif "Users" in tables:
    users = con.execute("select count(*) from Users").fetchone()[0]
    con.execute("delete from RefreshTokens")
    con.execute("delete from Users")
    con.commit()
    print(f"{sys.argv[1]}: {users} alte Benutzer und alle Refresh-Tokens gelöscht.")
else:
    print(f"{sys.argv[1]}: noch keine Benutzertabelle, nichts zu tun.")
PY
done

[ "$found" = 1 ] || echo "Keine homecontrol.db gefunden. Nichts zu tun (beim ersten Start wird alles neu angelegt)."
