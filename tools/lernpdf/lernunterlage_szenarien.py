from lernpdf import Doc

d = Doc("Szenarien", "Vom Türsignal zum bestätigten Scharfschalten",
        footer="Home Control - Szenarien - Stand 08.11.2026")
d.h1("1  Was ist eine Regel?")
d.p("Eine Regel verbindet einen Auslöser mit einer Aktion: Wenn die Flurtür aufgeht, wird ein Blink-System scharfgeschaltet. "
    "Sie läuft im Backend, deshalb funktioniert sie auch bei geschlossenem Browser. Neue Regeln starten ausgeschaltet.")
d.h1("2  Flanke statt Zustand")
d.p("Eine Tür kann lange offen stehen. Ausgelöst werden soll aber nur im Moment des Öffnens. Das nennt man Flanke: "
    "Der vorige Stand war Zu, der neue ist Auf. Ein gleichbleibender Zustand ist keine Flanke.")
d.table([['Meldung', 'Auslöser?', 'Warum'],
         ['Zu, dann Auf', 'Ja', 'Echter Wechsel'],
         ['Auf, dann Auf', 'Nein', 'Doppelte Meldung'],
         ['Erste Meldung ist Auf', 'Nein', 'Vorheriger Stand unbekannt'],
         ['Auf nach Verbindungslücke', 'Nein', 'Der Wechsel kann alt sein']], [0.36, 0.14, 0.50])
d.h1("3  Warum eine Warteschlange?")
d.p("Das Scharfschalten kann bei Blink bis zu 40 Sekunden dauern. Die App, die den Türzustand meldet, darf nicht so lange warten. "
    "Deshalb legt das Backend den Auslöser in eine Warteschlange, und ein Hintergrunddienst arbeitet sie ab.")
d.h1("4  Bestätigt heißt zurückgelesen")
d.p("Dass Blink einen Befehl annimmt, beweist noch nichts. Home Control liest den Zustand danach neu. Nur wenn Blink das System dann als scharf meldet, steht im Verlauf „Bestätigt“.")
d.h1("5  Für die Präsentation merken")
d.table([['Frage', 'Antwort'],
         ['Warum startet eine Regel ausgeschaltet?', 'Damit nichts ungewollt passiert, bevor sie geprüft ist.'],
         ['Was verhindert doppeltes Auslösen?', 'Flankenerkennung und eine Sperrzeit von 60 Sekunden.'],
         ['Woher kommt das Türsignal?', 'Über Muju Home aus Google Home, weil es dort sofort ankommt.']], [0.44, 0.56])
d.save("docs/Lernunterlage-Szenarien.pdf")
