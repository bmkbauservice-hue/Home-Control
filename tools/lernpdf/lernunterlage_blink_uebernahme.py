from lernpdf import Doc

d = Doc("Blink-Token sicher umziehen", "Warum nur eine Instanz Blink benutzen darf",
        footer="Home Control - Blink-Übernahme - Stand 08.11.2026")
d.h1("1  Das Problem: Rotation")
d.p("Blink gibt bei jeder Erneuerung ein neues Refresh-Token aus. Das alte wird dabei ungültig. "
    "Nutzen zwei Programme dasselbe Token, verbraucht das eine es dem anderen. Dann ist eine neue Anmeldung mit PIN nötig.")
d.h1("2  Die Lösung: genau ein Halter")
d.table([['Schritt', 'Was passiert', 'Warum'],
         ['1', 'Blink im Dauerdienst aus', 'Er kann das Token danach nicht mehr verändern'],
         ['2', 'Token in die Testdatenbank kopieren', 'Nur wenn es neuer ist, nie umgekehrt'],
         ['3', 'Testinstanz mit --mit-blink starten', 'Der Starter prüft vorher Schritt 1 und 2'],
         ['4', 'Später zurückgeben: zum-dienst', 'Das zuletzt erneuerte Token geht zurück']], [0.12, 0.46, 0.42])
d.h1("3  Warum das Token verschlüsselt bleiben kann")
d.p("Das Token liegt mit der ASP.NET Data Protection verschlüsselt in der Datenbank. Beide Instanzen benutzen denselben Schlüsselordner "
    "und denselben Anwendungsnamen. Deshalb kann das Werkzeug den verschlüsselten Wert kopieren, ohne ihn je im Klartext zu sehen.")
d.h1("4  Grenzen der Blink-Anbindung")
d.p("Blink veröffentlicht keine offizielle Schnittstelle. Home Control nutzt dieselben Wege wie das Projekt blinkpy. "
    "Blink kann sie jederzeit ändern. Vorschaubilder sind das letzte vorhandene Bild, kein Livebild. Die Aufnahmezeit steht deshalb immer dabei.")
d.h1("5  Für die Präsentation merken")
d.table([['Frage', 'Antwort'],
         ['Warum war Blink im Test aus?', 'Damit Test und Dauerdienst sich nicht gegenseitig aussperren.'],
         ['Sieht der Browser das Blink-Token?', 'Nein. Das Backend holt das Bild und gibt nur das Bild weiter.'],
         ['Macht das Anzeigen ein neues Foto?', 'Nein. Nur der Knopf „Neues Bild“, und der kostet Akku.']], [0.42, 0.58])
d.save("docs/Lernunterlage-Blink-Uebernahme.pdf")
