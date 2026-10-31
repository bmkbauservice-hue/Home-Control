"""Erzeugt docs/Lernunterlage-Betrieb.pdf (Aufruf aus dem Repo-Hauptordner:
PYTHONPATH=tools/lernpdf python3 tools/lernpdf/lernunterlage_betrieb.py)."""
from lernpdf import Doc

d = Doc(
    "Betrieb: Dienst und HTTPS",
    "Lernunterlage: Home Control läuft von allein und ist am iPhone als App erreichbar",
    footer="Home Control · Betrieb · Lernunterlage",
)

# ------------------------------------------------------------------ 1
d.h1("1  Vom Entwickeln zum Betrieb")
d.table([
    ["", "Entwickeln", "Betrieb"],
    ["Start", "dotnet run und npm run dev, von Hand", "systemd startet den Dienst beim Hochfahren"],
    ["Build", "Debug, ändert sich bei jedem Speichern", "Release per dotnet publish in eigenen Ordner"],
    ["Frontend", "Vite-Server mit Hot Reload (Port 5173)", "Fertige Dateien (npm run build), liefert das Backend aus"],
    ["Adresse", "http://localhost:5111", "https://&lt;rechner&gt;.&lt;tailnet&gt;.ts.net"],
    ["Absturz", "Terminal zeigt den Fehler, man startet neu", "systemd startet nach 5 Sekunden neu"],
], [0.16, 0.42, 0.42])
d.merksatz("<b>Merksatz:</b> Im Betrieb startet niemand mehr etwas von Hand. Ein <b>Dienst</b> übernimmt "
           "Starten, Neustarten und Protokollieren.")

# ------------------------------------------------------------------ 2
d.h1("2  systemd und Benutzerdienste")
d.p("<b>systemd</b> ist unter Linux das Programm, das beim Hochfahren alle Dienste startet und überwacht. "
    "Ein Dienst wird in einer <b>Unit-Datei</b> beschrieben. Ein <b>Benutzerdienst</b> "
    "(<font name='Mono'>systemctl --user</font>) läuft mit deinen Rechten und braucht kein sudo.")
d.code("""[Unit]
Description=Home Control (Oberfläche und API)
After=network-online.target              # erst starten, wenn das Netz da ist

[Service]
WorkingDirectory=~/.local/share/home-control/app     # appsettings.json und wwwroot liegen hier
ExecStart=/usr/lib/dotnet/dotnet .../HomeControl.Api.dll
Environment=ASPNETCORE_ENVIRONMENT=Development
Environment=ASPNETCORE_URLS=http://127.0.0.1:5200    # nur lokal erreichbar
Environment="ConnectionStrings__DefaultConnection=Data Source=.../homecontrol.db"
Restart=on-failure                        # nach Absturz neu starten
RestartSec=5

[Install]
WantedBy=default.target                   # beim Start der Benutzersitzung mitstarten""")
d.bullets([
    "<b>Voller Pfad zu dotnet:</b> Ein Dienst kennt den PATH aus dem Terminal nicht.",
    "<b>Doppelter Unterstrich:</b> <font name='Mono'>ConnectionStrings__DefaultConnection</font> überschreibt "
    "<font name='Mono'>ConnectionStrings:DefaultConnection</font> aus appsettings.json. Umgebungsvariablen "
    "schlagen appsettings.json.",
    "<b>Linger:</b> <font name='Mono'>loginctl enable-linger</font> lässt Benutzerdienste schon beim Hochfahren "
    "starten, nicht erst nach der Anmeldung.",
    "<b>Inhaltsordner:</b> ASP.NET Core sucht appsettings.json im Arbeitsordner. Deshalb zeigt "
    "WorkingDirectory auf den Publish-Ordner.",
])
d.table([
    ["Befehl", "Wozu"],
    ["systemctl --user status home-control", "Läuft er? Seit wann? Letzte Log-Zeilen"],
    ["journalctl --user -u home-control -f", "Log live mitlesen"],
    ["systemctl --user restart home-control", "Neu starten (z. B. nach neuen User Secrets)"],
    ["systemctl --user disable --now home-control", "Ausschalten und nicht mehr automatisch starten"],
], [0.5, 0.5])

# ------------------------------------------------------------------ 3
d.h1("3  dotnet publish und Ports")
d.p("<font name='Mono'>dotnet publish -c Release -o &lt;ordner&gt;</font> baut eine optimierte Fassung und kopiert "
    "alles, was zum Laufen nötig ist, in einen eigenen Ordner. Der Dienst läuft von dort. So stört es ihn nicht, "
    "wenn du im Repo weiterarbeitest. Ein Update ist einfach: <font name='Mono'>./dienst-einrichten.sh</font> "
    "noch einmal ausführen.")
d.merksatz("<b>Warum Port 5200?</b> Ein Port kann nur von einem Programm belegt sein. Beim Entwickeln nutzt "
           "<font name='Mono'>dotnet run</font> Port 5111. Würde der Dienst auch 5111 nehmen, käme beim nächsten "
           "Entwickeln „address already in use“. Genau dieser Fehler war am 29.10. aufgetreten.", warn=True)

# ------------------------------------------------------------------ 4
d.h1("4  HTTPS über Tailscale")
d.p("Tailscale verbindet deine Geräte zu einem privaten Netz (VPN). <font name='Mono'>tailscale serve</font> "
    "ist ein kleiner <b>Reverse Proxy</b>: Er nimmt HTTPS-Anfragen an deinem Tailscale-Namen an und reicht sie "
    "an den Dienst auf 127.0.0.1:5200 weiter.")
d.code("""iPhone (Safari)  ──HTTPS──▶  tailscale serve  ──HTTP──▶  Home Control (127.0.0.1:5200)
                 https://muju-ms-7c95.<tailnet>.ts.net         nur auf diesem Rechner erreichbar""")
d.bullets([
    "<b>Echtes Zertifikat:</b> Tailscale holt es selbst (Let's Encrypt) für den .ts.net-Namen. Safari zeigt "
    "keine Warnung.",
    "<b>Nur im eigenen Netz:</b> Erreichbar ist der Link nur für Geräte in deinem Tailscale-Netz, nicht für "
    "das Internet.",
    "<b>Warum überhaupt HTTPS?</b> Ein Service Worker (PWA, App auf dem Home-Bildschirm) läuft nur über HTTPS. "
    "Und Passwort sowie Tokens sollen nie unverschlüsselt durchs Netz gehen.",
])

# ------------------------------------------------------------------ 5
d.h1("5  Bewusste Grenzen")
d.table([
    ["Grenze", "Warum so, und was wäre besser"],
    ["Umgebung Development", "Die Geheimnisse liegen in den User Secrets, und die liest ASP.NET Core nur im "
     "Development. Besser für einen Server: Production und Geheimnisse als Umgebungsvariablen."],
    ["Cookie nicht Secure", "Folgt aus Development. Über den HTTPS-Link wird es trotzdem verschlüsselt übertragen."],
    ["/scalar erreichbar", "Die API-Doku ist im Development an. Erreichbar nur im Tailscale-Netz."],
    ["PC muss laufen", "Variante B: alles auf den OVH-Server (Docker, Reverse Proxy), läuft dann rund um die Uhr."],
], [0.28, 0.72])

# ------------------------------------------------------------------ 6
d.h1("6  Kurzantworten für Rückfragen")
d.table([
    ["Frage", "Antwort"],
    ["Was passiert, wenn der Server abstürzt?", "systemd startet ihn nach 5 Sekunden neu (Restart=on-failure)."],
    ["Und nach einem Stromausfall?", "Linger ist an, der Dienst startet beim Hochfahren automatisch."],
    ["Wie kommt das iPhone an die App?", "Über Tailscale per HTTPS-Link, dann „Zum Home-Bildschirm“."],
    ["Ist die App aus dem Internet erreichbar?", "Nein. Der Dienst hört nur auf 127.0.0.1, von außen geht es nur über das Tailscale-Netz."],
    ["Wie spielst du ein Update ein?", "git pull, dann ./dienst-einrichten.sh: baut neu und startet neu."],
], [0.4, 0.6])

d.h1("7  Merksätze")
d.merksatz("• Dienst = systemd startet, überwacht und protokolliert.<br/>"
           "• Benutzerdienst: systemctl --user, kein sudo. Linger: startet beim Hochfahren.<br/>"
           "• Umgebungsvariable mit __ überschreibt appsettings.json (: wird zu __).<br/>"
           "• Ein Port, ein Programm: Dienst 5200, Entwicklung 5111.<br/>"
           "• tailscale serve = Reverse Proxy mit echtem HTTPS, nur im eigenen Netz.")

d.save("docs/Lernunterlage-Betrieb.pdf")
