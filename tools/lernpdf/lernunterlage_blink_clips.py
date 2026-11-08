from lernpdf import Doc

d = Doc("Blink-Clips erkunden", "Erst den echten Zugriffsweg belegen, dann bauen",
        footer="Home Control - Blink-Clips - Stand 09.11.2026")
d.h1("1  Warum zuerst erkunden?")
d.p("Blink hat keine offizielle Schnittstelle. Ob Clips erreichbar sind, hängt vom Sync Module, vom USB-Stick und vom Abo ab. "
    "Deshalb wurde zuerst am echten Konto geprüft, welcher Weg funktioniert. Erst danach wird die Alarmseite gebaut.")
d.h1("2  Das Ergebnis")
d.table([['Prüfung', 'Ergebnis'],
         ['Sync Module', 'online, lokaler Speicher aktiv'],
         ['Clip-Liste vom Stick', '7 Clips'],
         ['Ein Clip', 'echte MP4, H.264 und AAC, 7,07 Sekunden'],
         ['Cloud-Liste', 'erfolgreich abgefragt, 0 Einträge'],
         ['Abo-Angaben', 'nur ein Zeitstempel, kein Abo erkennbar']], [0.40, 0.60])
d.h1("3  Wie kommt ein Clip vom Stick?")
d.p("Der Stick steckt am Sync Module, nicht am Rechner. Home Control fordert zuerst eine Liste an, das Manifest. "
    "Danach bittet es das Sync Module, einen einzelnen Clip bereitzustellen, und lädt ihn über das Blink-Konto. Auf dem Stick ändert sich dabei nichts.")
d.h1("4  Woran erkennt man eine echte MP4?")
d.p("Eine MP4-Datei trägt ab Byte 4 die Kennung ftyp. Das Werkzeug ffprobe zeigt Format, Codec, Auflösung und Dauer. "
    "ffmpeg dekodiert die Datei vollständig. Hier gelangen 105 von 106 Videopaketen, ein leeres Paket enthielt kein Bild.")
d.h1("5  Für die Präsentation merken")
d.table([['Frage', 'Antwort'],
         ['Direkter Speicher oder Cloud-Backup?', 'Direkter lokaler Speicher. Für ein Backup gab es keinen Beleg.'],
         ['Was bleibt unverändert?', 'Die Originale auf dem Stick. Gelöscht wird nichts.'],
         ['Warum eine Wegwerf-Kopie?', 'Damit vor der Entscheidung kein Code ins Projekt kommt und nur ein Prozess das Blink-Token nutzt.']], [0.42, 0.58])
d.save("docs/Lernunterlage-Blink-Clips.pdf")
