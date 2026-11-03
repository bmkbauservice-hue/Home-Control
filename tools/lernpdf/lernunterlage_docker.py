"""Erzeugt docs/Lernunterlage-Docker.pdf (Aufruf aus dem Repo-Hauptordner:
PYTHONPATH=tools/lernpdf python3 tools/lernpdf/lernunterlage_docker.py)."""
from lernpdf import Doc

M = "<font name='Mono'>"
E = "</font>"

d = Doc(
    "Docker",
    "Lernunterlage: Image und Container, Layer, Volumes, Multi-Stage-Build, warum nicht root, Secrets nicht ins Image",
    footer="Home Control · Docker · Lernunterlage",
)

# ------------------------------------------------------------------ 1
d.h1("1  Worum es geht")
d.p("Home Control läuft bisher als Dienst direkt auf dem PC. Mit Docker gibt es eine zweite Möglichkeit: Die App "
    "steckt mit allem, was sie braucht (.NET-Laufzeit, gebaute Oberfläche), in einem <b>Container</b>. Der startet "
    "auf jedem Rechner mit Docker gleich, egal welches .NET oder Node dort installiert ist. Der Dienst auf dem PC "
    "bleibt, wie er ist.")
d.code("""docker compose --profile demo up   # Demo, ohne Geheimnisse, http://localhost:5300
cp .env.example .env                # Betrieb: Werte eintragen
docker compose up -d                # Betrieb, http://localhost:5400""")

# ------------------------------------------------------------------ 2
d.h1("2  Image und Container")
d.table([
    ["Begriff", "Erklärung", "Vergleich"],
    ["Image", "unveränderlicher Bauplan mit Dateien und Startbefehl, entsteht mit docker build", "Klasse / Backform"],
    ["Container", "ein laufendes Exemplar eines Images, mit eigenem Dateisystem und Netz", "Objekt / Kuchen"],
    ["Dockerfile", "Rezept, aus dem das Image gebaut wird", "Quellcode"],
    ["Volume", "Speicher außerhalb des Containers, überlebt ihn", "externe Festplatte"],
    ["Compose", "beschreibt mehrere Container, Ports, Volumes in einer YAML-Datei", "Startskript"],
], [0.17, 0.55, 0.28])
d.p("Ein Container ist zum <b>Wegwerfen</b> gedacht: Bei einem Update baut man ein neues Image und startet einen "
    "neuen Container. Alles, was bleiben soll (Datenbank, Schlüssel), gehört deshalb in ein Volume.")

# ------------------------------------------------------------------ 3
d.h1("3  Layer und Cache")
d.p("Jede Anweisung im Dockerfile erzeugt eine <b>Schicht</b> (Layer). Docker merkt sich die Schichten. Hat sich "
    "an einer Zeile und an allem davor nichts geändert, kommt die Schicht aus dem Cache. Deshalb die Reihenfolge:")
d.code("""COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci                 # teuer, aber nur neu, wenn sich die Paketliste ändert
COPY frontend/ ./
RUN npm run build          # bei jeder Codeänderung neu, geht schnell""")
d.p("Würde man zuerst den ganzen Code kopieren, müsste " + M + "npm ci" + E + " bei jeder kleinen Änderung neu laufen. "
    "Beim Backend gilt dasselbe: erst die " + M + ".csproj" + E + " und " + M + "dotnet restore" + E + ", dann der Code.")

# ------------------------------------------------------------------ 4
d.h1("4  Multi-Stage-Build")
d.table([
    ["Stufe", "Basis-Image", "Ergebnis", "Im fertigen Image?"],
    ["1 frontend", "node:22-alpine", "frontend/dist (HTML, JS, CSS)", "nur dist"],
    ["2 backend", "dotnet/sdk:10.0", "/app (veröffentlichte DLLs)", "nur /app"],
    ["3 final", "dotnet/aspnet:10.0", "Laufzeit + /app + wwwroot", "ja"],
], [0.16, 0.24, 0.36, 0.24])
d.bullets([
    "Nur die <b>letzte Stufe</b> wird zum Image. Node, das .NET SDK (1,3 GB) und der Quellcode bleiben zurück.",
    "Kleiner: 409 MB statt über 1,3 GB. Davon sind 342 MB das aspnet-Basis-Image, Home Control selbst ist der kleine Rest.",
    "Sicherer: Im Image gibt es keinen Compiler und kein npm, die ein Angreifer benutzen könnte.",
    M + "COPY --from=backend /app ./" + E + " holt gezielt nur das Ergebnis aus einer früheren Stufe.",
])

# ------------------------------------------------------------------ 5
d.h1("5  Volumes")
d.table([
    ["Volume", "Im Container", "Inhalt", "Warum eigenes Volume?"],
    ["homecontrol-daten", "/data", "homecontrol.db", "Daten überleben Updates"],
    ["homecontrol-schluessel", "/keys", "Data-Protection-Schlüssel", "sonst ist das Blink-Token nach einem Update unlesbar"],
], [0.25, 0.14, 0.26, 0.35])
d.p("Getestet: Benutzer angelegt, Container mit " + M + "--force-recreate" + E + " neu erzeugt, Anmeldung klappt "
    "weiter. Die Demo benutzt dagegen <b>tmpfs</b> (nur Arbeitsspeicher): Jeder Start beginnt mit dem frischen "
    "Beispielhaus.")
d.p("Der Pfad der Schlüssel ist dafür einstellbar geworden: " + M + "HomeControl:DataProtectionKeysPath" + E +
    " (im Image " + M + "/keys" + E + "). Ohne Einstellung bleibt alles wie beim PC-Dienst.")

# ------------------------------------------------------------------ 6
d.h1("6  Warum nicht als root?")
d.bullets([
    "Standardmäßig läuft ein Prozess im Container als <b>root</b>. Findet ein Angreifer eine Lücke in der App, hätte "
    "er im Container alle Rechte und einen kürzeren Weg zum Rechner dahinter.",
    M + "USER $APP_UID" + E + ": Das aspnet-Image bringt den Benutzer " + M + "app" + E + " (UID 1654) mit. Die App "
    "läuft mit dessen Rechten und darf nur in " + M + "/data" + E + " und " + M + "/keys" + E + " schreiben.",
    "Port 8080 statt 80: Ports unter 1024 dürfte nur root öffnen.",
    "Geprüft: " + M + "docker run --entrypoint id homecontrol" + E + " zeigt " + M + "uid=1654(app)" + E + ", "
    "die Schlüsseldatei im Volume hat Rechte 600.",
])

# ------------------------------------------------------------------ 7
d.h1("7  Secrets nicht ins Image")
d.p("Ein Image kann man weitergeben, hochladen, auf einen Server kopieren. Was darin steckt, ist für jeden lesbar, "
    "der das Image hat, auch alte Schichten. Deshalb:")
d.bullets([
    "<b>Kein Geheimnis im Dockerfile:</b> kein " + M + "COPY .env" + E + ", kein Schlüssel in " + M + "ENV" + E + ".",
    M + ".dockerignore" + E + " schließt " + M + ".env" + E + " und alle " + M + "*.db" + E + " aus: Sie gelangen nicht "
    "einmal in den Build-Kontext.",
    "Geheimnisse kommen erst beim <b>Start</b>: " + M + "env_file: .env" + E + " in docker-compose.yml.",
    M + ".env.example" + E + " ist die Vorlage im Repo, <b>ohne Werte</b>. Die echte " + M + ".env" + E + " steht in "
    ".gitignore.",
    "Ein Test prüft das bei jedem " + M + "./pruefen.sh" + E + ": .env in .dockerignore und .gitignore, leere Werte "
    "in der Vorlage, " + M + "USER $APP_UID" + E + " im Dockerfile.",
])
d.merksatz("Das Image ist der Bauplan und darf öffentlich sein. Geheimnisse gehören in die Umgebung beim Start, "
           "nie in den Bauplan.", warn=True)

# ------------------------------------------------------------------ 8
d.h1("8  Healthcheck und Profile")
d.bullets([
    "<b>Healthcheck:</b> Docker fragt alle 30 Sekunden, ob die App gesund ist. Das aspnet-Image hat kein curl, deshalb "
    "prüft sich die App selbst: " + M + "dotnet HomeControl.Api.dll --healthcheck" + E + " fragt " + M + "/healthz" + E +
    " (läuft, Datenbank erreichbar) und endet mit 0 oder 1. " + M + "docker compose ps" + E + " zeigt dann „healthy“.",
    "<b>Profile:</b> " + M + "betrieb" + E + " (echter Dienst) und " + M + "demo" + E + ". In der .env steht "
    + M + "COMPOSE_PROFILES=betrieb" + E + ", deshalb startet " + M + "docker compose up" + E + " den Betrieb. "
    + M + "--profile demo" + E + " startet nur die Demo, ganz ohne .env, also auch auf dem Schul-PC.",
    "<b>Commit:</b> Im Container gibt es kein .git. Der Build-Parameter " + M + "GIT_COMMIT" + E + " wird zu "
    + M + "-p:SourceRevisionId" + E + ", die Einstellungsseite zeigt ihn an.",
])

# ------------------------------------------------------------------ 9
d.h1("9  Kurzantworten für Rückfragen")
d.table([
    ["Frage", "Antwort"],
    ["Unterschied Image und Container?", "Image = Bauplan (unveränderlich), Container = laufendes Exemplar davon."],
    ["Was ist ein Layer?", "Eine Schicht pro Anweisung im Dockerfile. Unveränderte Schichten kommen aus dem Cache."],
    ["Warum Multi-Stage?", "Werkzeuge nur zum Bauen, im Image nur die Laufzeit: kleiner und sicherer."],
    ["Warum Volumes?", "Container sind Wegwerfware. Datenbank und Schlüssel sollen Updates überleben."],
    ["Warum nicht root?", "Eine Lücke in der App gibt dann nur die Rechte des Benutzers app, nicht alle."],
    ["Wo sind die Passwörter?", "In .env, nur beim Start. Nie im Image, nie im Repo."],
    ["Ersetzt Docker den Dienst?", "Nein, zusätzlich. Blink darf nur an einer Stelle laufen, deshalb im Container aus."],
    ["Wie weiß Docker, ob es läuft?", "Healthcheck: die App fragt ihren eigenen Endpunkt /healthz ab."],
], [0.34, 0.66])

d.h1("10  Merksätze")
d.merksatz("• Image = Bauplan, Container = laufendes Exemplar, Volume = was bleiben soll.<br/>"
           "• Selten Geändertes zuerst kopieren, dann nutzt Docker den Cache.<br/>"
           "• Multi-Stage: bauen mit dem SDK, ausliefern mit der Laufzeit.<br/>"
           "• Nie als root, nie Geheimnisse im Image.<br/>"
           "• Eine Funktion erst dokumentieren, wenn sie getestet ist (Start, Neustart, Sicherung).")

d.save("docs/Lernunterlage-Docker.pdf")
