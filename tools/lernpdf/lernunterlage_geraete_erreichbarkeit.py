from lernpdf import Doc

d = Doc("Welche Geräte erreicht Home Control?", "Berechtigung, offline, Schaltfunktion und Messwert unterscheiden",
        footer="Home Control - Geräte-Erreichbarkeit - Stand 08.11.2026")
d.h1("1  Warum reicht „geht nicht“ nicht?")
d.p("Ein Smart-Home-Gerät kann aus sehr verschiedenen Gründen nicht reagieren. Wer die Ursache verwechselt, repariert das Falsche. "
    "Home Control nennt deshalb beim Schalten die genaue Ursache und sendet in diesen Fällen keinen Befehl.")
d.table([['Befund', 'Bedeutung', 'Beispiel'],
         ['Fehlende Berechtigung', 'Gerät kann aktiv sein, das Tuya-Projekt darf es aber nicht abfragen', 'Code 1106'],
         ['Offline', 'Tuya kennt das Gerät, erreicht es aber gerade nicht', 'Barlicht'],
         ['Fehlende Schaltfunktion', 'Gerät erreichbar, meldet aber kein switch_1', 'Türsensor'],
         ['Fehlender Messwert', 'Gerät schaltbar, meldet aber keine Leistung', 'Heizdecke']], [0.27, 0.53, 0.20])
d.h1("2  Der Beweis an der Stehlampe")
d.p("Die Tuya-Cloud lehnt die Lidl-ID mit 1106 ab. Trotzdem meldet sich die Steckdose im WLAN unter genau dieser ID. "
    "Damit ist belegt: Sie ist nicht offline, dem Projekt fehlt die Berechtigung.")
d.h1("3  Passiv lauschen")
d.p("Tuya-Geräte senden regelmäßig UDP-Rundrufe ins Heimnetz. Das Werkzeug tuya-lan-lauschen.py hört nur zu und sendet nichts. "
    "Der Schlüssel für diese Rundrufe ist öffentlich dokumentiert. Steuern lässt sich damit nichts, denn dafür braucht jedes Gerät seinen eigenen local_key.")
d.h1("4  Warum nicht einfach lokal schalten?")
d.p("Den local_key gibt Tuya nur für Geräte heraus, die das eigene Cloud-Projekt sehen darf. Dafür müsste die Steckdose in ein "
    "verknüpftes Konto von Tuya Smart oder Smart Life umziehen. Das hieße neu koppeln, und das ist bewusst nicht gewünscht.")
d.h1("5  Nächster Weg: Google Home")
d.p("Die Android-Test-App kennt den Trait OnOff: Sie liest den Zustand und kann on() und off() aufrufen. Der Rückweg von Home Control "
    "zur App wird erst gebaut, wenn die Stehlampe dort nachweislich mit Ein/Aus erscheint. Erst Beleg, dann Code.")
d.h1("6  Für die Präsentation merken")
d.table([['Frage', 'Antwort'],
         ['Was bedeutet 1106?', 'Keine Berechtigung, nicht offline.'],
         ['Wann gilt ein Schaltvorgang als erfolgreich?', 'Erst wenn der neue Zustand zurückgelesen wurde.'],
         ['Warum zeigt eine Steckdose keine Watt?', 'Schaltbar heißt nicht messfähig. Das Gerät muss cur_power melden.'],
         ['Warum zählt ein alter 0-Watt-Wert nicht?', 'Ein Wert zählt nur von einem Online-Gerät und höchstens 30 Minuten alt.']], [0.42, 0.58])
d.save("docs/Lernunterlage-Geraete-Erreichbarkeit.pdf")
