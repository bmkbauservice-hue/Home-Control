from lernpdf import Doc

d = Doc("Schalten über Google Home", "Polling, Bestätigung und Schutz vor alten Befehlen",
        footer="Home Control - Google Home schalten - Stand 08.11.2026")
d.h1("1  Das Problem")
d.p("Die Stehlampe hängt an einer Lidl-Home-Steckdose. Das Tuya-Projekt bekommt keinen Zugriff (Code 1106). "
    "Google Home kann sie schalten. Die Test-App Muju Home nutzt dieselbe Schnittstelle.")
d.h1("2  Warum fragt die App nach?")
d.p("Das Backend kann das Handy nicht von sich aus erreichen. Deshalb fragt die App alle 1,5 Sekunden nach, ob ein Befehl vorliegt. "
    "Das nennt man Polling. Mit derselben Anfrage meldet sie den aktuellen Zustand ihrer Geräte.")
d.table([['Schritt', 'Wer', 'Was'],
         ['1', 'Browser', 'Admin klickt den Schalter'],
         ['2', 'Backend', 'legt den Befehl mit Frist bereit und wartet'],
         ['3', 'Muju Home', 'holt ihn ab und schaltet über Google Home'],
         ['4', 'Muju Home', 'liest den neuen Zustand und meldet ihn'],
         ['5', 'Backend', 'speichert erst jetzt Ein oder Aus']], [0.12, 0.22, 0.66])
d.h1("3  Wann gilt ein Schaltvorgang als erfolgreich?")
d.p("Nicht wenn der Befehl abgeschickt wurde, sondern erst, wenn das Gerät den neuen Zustand gemeldet hat. "
    "Bleibt die Meldung aus, zeigt Home Control einen Fehler und lässt den alten Zustand stehen.")
d.h1("4  Warum keine alten Befehle?")
d.p("Stell dir vor, das Handy ist getrennt und jemand klickt auf Ein. Eine Stunde später verbindet es sich wieder. "
    "Ginge der Befehl jetzt noch raus, schaltete sich die Lampe ohne Grund ein. "
    "Darum hat jeder Befehl eine Abholfrist von 4 Sekunden, wird genau einmal ausgeliefert und liegt nur im Arbeitsspeicher.")
d.h1("5  Absicherung")
d.table([['Schutz', 'Wogegen'],
         ['Nur Admin', 'Normale Benutzer dürfen nicht schalten'],
         ['Loopback und Bridge-Schlüssel', 'Fremde im Netz können keine Geräte melden oder Befehle abholen'],
         ['Vergleich in fester Zeit', 'Die Antwortzeit verrät nichts über den Schlüssel'],
         ['Nur verknüpfte Geräte', 'Andere Geräte im Google-Konto bekommen nie einen Befehl'],
         ['Gesperrte Namen', 'Die PC-Steckdose lässt sich nicht verknüpfen']], [0.36, 0.64])
d.h1("6  Zwei Anbindungen, eine Regel")
d.p("Die Stehlampe hat noch eine alte Tuya-ID. Sobald eine Google-Home-Verknüpfung besteht, gilt nur noch diese. "
    "Der Tuya-Abgleich überspringt das Gerät, sonst würde ein alter Zustand den echten überschreiben.")
d.h1("7  Letzter Stand ist nicht aktueller Stand")
d.p("Tuya meldete beim Türsensor 90 % Akku, obwohl gerade neue Batterien eingesetzt wurden. Die Cloud liefert immer den letzten gemeldeten Wert. "
    "Hier stammte er vom 02.11. Deshalb speichert Home Control zusätzlich die Meldezeit und markiert alte Werte. Für die Temperatursensoren liefert Google Home gar keinen Akkustand, also zeigt Home Control auch keinen an.")
d.h1("8  Für die Präsentation merken")
d.p("Software-Tests belegen die Logik mit einer gespielten App. Ob die echte Lampe reagiert, zeigt nur der Test am Gerät. "
    "Beides getrennt zu berichten, gehört zu sauberer Arbeit.")
d.save("docs/Lernunterlage-Google-Home-Schalten.pdf")
