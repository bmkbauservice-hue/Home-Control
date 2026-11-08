from lernpdf import Doc

d = Doc(
    "Alarm mit Blink und Türsensor",
    "Home Control | Türregel bedienen, prüfen und erklären",
    footer="Home Control | Lernunterlage Alarm | Stand 09.11.2026",
)

d.merksatz(
    "<b>Am echten Gerät bestätigt:</b> Wenn die Regel eingeschaltet ist und die Flurtür aufgeht, "
    "schaltet Home Control das zugeordnete Blink-System scharf. Beide Kameras lieferten über "
    "„Neues Bild“ außerdem ein frisches Standbild."
)
d.h1("1  Was passiert beim Öffnen der Tür?")
d.merksatz(
    "<b>Flurtür öffnet sich</b> → Muju Home meldet den Wechsel von Zu auf Auf → "
    "Home Control prüft die Regel → Blink schaltet scharf → Home Control liest den Zustand zurück."
)
d.p(
    "Der <b>Auslöser</b> ist der Zustandswechsel des Türsensors. Die <b>Aktion</b> ist das "
    "Scharfschalten des gewählten Blink-Systems. Im Verlauf bedeutet „Bestätigt“, dass Blink "
    "beim erneuten Lesen den Zustand „scharf“ gemeldet hat."
)

d.h1("2  So testest du die Regel")
d.numbered([
    "Verbinde das Android-Handy per USB und lass <b>Muju Home</b> mit dem richtigen Zuhause geöffnet.",
    "Öffne in Home Control <b>Szenarien</b>. Wähle den Türsensor im Flur und das gewünschte Blink-System.",
    "Schließe die Flurtür und prüfe die Anzeige „Zu“. Aktiviere die Regel und öffne die Tür einmal.",
    "Prüfe den neuen Eintrag im Verlauf. Schalte die Regel nach dem Test aus, wenn sie nicht weiterlaufen soll. "
    "Soll das Blink-System wieder unscharf sein, entschärfe es separat in der Blink-App.",
])

d.h1("3  Wenn der Test nicht klappt")
d.table([
    ("Was sehe ich?", "Was prüfe ich?"),
    ("Kein neuer Verlaufseintrag", "Regel aktiv? Meldet der richtige Türsensor einen frischen Wechsel? "
     "USB-Verbindung und Muju Home prüfen."),
    ("Eintrag vorhanden, Bestätigung fehlt", "Meldung im Verlauf lesen und den Systemzustand in Blink prüfen."),
    ("Die Tür war schon offen", "Auf eine frische Meldung „Zu“ warten und erneut öffnen. Alte und doppelte "
     "Meldungen sollen keine neue Aktion auslösen."),
], [0.37, 0.63])

d.page_break()
d.h1("4  Scharf, Standbild und Video unterscheiden")
d.table([
    ("Begriff", "Was er im Projekt bedeutet"),
    ("Scharf", "Blink ist für die Bewegungsüberwachung scharfgeschaltet. Das Öffnen der Tür kann "
     "diesen Zustand auslösen. Ein Video zum selben Zeitpunkt ist damit noch nicht belegt."),
    ("Standbild", "„Neues Bild“ fordert ein Foto an. Unter dem Bild erkennst du an der Aufnahmezeit, "
     "ob die Vorschau aktualisiert wurde."),
    ("Bewegungsclip", "Ein von Blink gespeichertes Video. Dafür müssen die Bewegungserkennung und "
     "eine Speicheroption eingerichtet sein."),
    ("Livebild", "Ein laufender Videostream. Die lokale Probe kann Videodaten prüfen. Die Wiedergabe "
     "in Home Control ist noch nicht bestätigt."),
], [0.25, 0.75])

d.h1("5  USB-Stick und geplante Alarmseite")
d.p(
    "Beim Sync Module 2 können geeignete Kameras Bewegungsclips als MP4 auf USB speichern. "
    "Blink unterscheidet direkten lokalen Speicher und die tägliche USB-Sicherung von Cloud-Clips. "
    "Zum manuellen Kopieren den Stick zuerst in der Blink-App sicher auswerfen."
)
d.p(
    "<b>Geplant:</b> Ein Menüpunkt „Alarm“ zeigt Türereignis, bestätigtes Scharfschalten und "
    "verfügbare Aufnahmen mit Kamera, Aufnahmezeit, Vorschau und Wiedergabe. Ein zeitlich "
    "passender Clip wird als solcher gekennzeichnet."
)
d.merksatz(
    "<b>Noch offen:</b> Ob Home Control bei eingestecktem USB-Stick einen echten Clip über das "
    "Sync Module abrufen und abspielen kann. Zuerst einen vorhandenen Clip lesend prüfen. "
    "Alternativ lassen sich die kopierten MP4-Dateien für einen manuellen Import nutzen.",
    warn=True,
)

d.h1("6  So kannst du es bei der Abgabe erklären")
d.p(
    "„Ich habe den Türkontakt mit Blink verknüpft. Bei aktiver Regel schaltet ein echter Wechsel "
    "von Zu auf Auf das Blink-System scharf. Im Verlauf sehe ich die Rückmeldung von Blink. "
    "Scharfschalten, Standbild und gespeicherter Videoclip sind drei getrennte Vorgänge.“"
)
d.p(
    'Quellen zur Vertiefung: <link href="https://support.blinkforhome.com/en_US/how-can-i-arm-my-system-for-motion-detection" color="#0e7490">Blink: Scharfschalten</link> · '
    '<link href="https://support.blinkforhome.com/de_DE/usando-sync-module-2/how-to-access-clip-backups-on-your-usb-drive" color="#0e7490">Blink: USB-Clips</link> · '
    '<link href="https://github.com/fronzbot/blinkpy/blob/dev/blinkpy/api.py" color="#0e7490">blinkpy: Clip-Abruf</link>'
)
d.save("docs/Lernunterlage-Alarm-Blink-und-Tuersensor.pdf")
