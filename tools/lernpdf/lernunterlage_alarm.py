from lernpdf import Doc

d = Doc("Die Alarmseite", "Ereignisse, Clips und ehrliche Zuordnung",
        footer="Home Control - Alarmseite - Stand 09.11.2026")
d.h1("1  Was zeigt die Seite?")
d.p("Oben steht der Verlauf aus den Szenarien: Die Tür ging auf, und Blink hat das Scharfschalten bestätigt oder nicht. "
    "Darunter stehen die Clips vom USB-Stick. Ein Ereignis erscheint auch dann, wenn es keinen Clip dazu gibt.")
d.h1("2  Zeitlich passend ist kein Beweis")
d.p("Ein Clip heißt „zeitlich passend“, wenn er zwischen 30 Sekunden vor und 3 Minuten nach dem Türereignis entstand. "
    "Das beschreibt nur die Zeit. Ob die Tür den Clip ausgelöst hat, weiß Home Control nicht und behauptet es deshalb auch nicht.")
d.h1("3  Warum lädt das Video erst beim Klick?")
d.p("Jeder Clip muss vom Sync Module über Blink bereitgestellt werden. Alle Clips vorab zu laden, wäre langsam und würde das Sync Module unnötig wecken. "
    "Bis zum ersten Abspielen steht deshalb ein Platzhalter dort, danach ein Standbild aus dem Video.")
d.h1("4  Der Browser spricht nur mit dem Backend")
d.table([['Schutz', 'Warum'],
         ['Nur Admin', 'Kameravideos sind privat'],
         ['Video über das Backend', 'Der Browser sieht weder Blink-Adressen noch Token'],
         ['no-store', 'Das Video landet nicht im Browser-Cache'],
         ['Kurzer Schlüssel statt Adresse', 'Der Browser kann keine fremden Adressen anfragen']], [0.38, 0.62])
d.h1("5  Ein echter Fehler, den nur der echte Test fand")
d.p("Die Testdaten liefen fehlerfrei. Beim echten Clip brach Chrome nach 3,7 Sekunden ab, weil die Kamera ein leeres Videopaket ohne Bild geschrieben hatte. "
    "Jetzt verpackt ffmpeg die heruntergeladene Kopie neu und lässt solche Pakete weg. Bild und Ton werden nicht neu kodiert, der Stick bleibt unverändert.")
d.h1("6  Für die Präsentation merken")
d.table([['Frage', 'Antwort'],
         ['Warum reichen Softwaretests nicht?', 'Echte Dateien können Fehler enthalten, die gespielte Daten nicht haben.'],
         ['Was passiert ohne ffmpeg?', 'Das Video kommt unverändert und kann an leeren Paketen abbrechen.'],
         ['Bleibt der Stick unverändert?', 'Ja. Verarbeitet wird nur eine kurzzeitige Kopie.']], [0.42, 0.58])
d.save("docs/Lernunterlage-Alarm.pdf")
