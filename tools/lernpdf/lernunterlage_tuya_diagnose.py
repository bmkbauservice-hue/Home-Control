from lernpdf import BODY, Doc

BODY.fontSize = 9.25
BODY.leading = 12.5

d = Doc("Tuya-Diagnose und Waage", "Home Control - Online-Status, Datenpunkte und letzte Messung",
        footer="Home Control - Tuya-Diagnose - Stand 06.11.2026")
d.h1("1  Was prüfen wir?")
d.p("Badlicht, Bar Licht und Stehlampe sollen zuverlässig schaltbar sein. Bei den bereits gelesenen Wohnzimmergeräten ist switch_1 vorhanden. Die Ursache der Offline-Anzeige ist noch offen. Die Silvergear Smart Scale soll eine eigene Ansicht mit der letzten tatsächlichen Messung erhalten.")
d.table([['Abruf', 'Was er beantwortet'], ['App-Geräteliste', 'Welche Geräte sind mit dem Cloud-Projekt verbunden?'],
         ['Direkte Geräteinformation', 'Welchen Online-Status liefert die Einzelabfrage?'],
         ['Status und Spezifikation', 'Welche Datenpunkte gibt es, und was bedeuten Einheit und Skalierung?'],
         ['Cloud-Eigenschaftswerte', 'Welche Werte wurden zuletzt gemeldet und zu welchem Zeitpunkt?']], [0.34, 0.66])
d.h1("2  Das Diagnosewerkzeug")
d.code("dotnet run --project tools/TuyaDiagnose")
d.p("Das Werkzeug nutzt vorhandene lokale Zugangsdaten. Es startet keinen Webserver, öffnet keine Datenbank und sendet keine Schaltbefehle. Die laufende Sensor-Bridge muss nicht neu gestartet werden.")
d.numbered(["Gerätenamen und Online-Status in der Liste lesen.",
            "Die gewünschten Gerätenummern mit Komma getrennt eingeben.",
            "Direkten Online-Status mit dem der App-Liste vergleichen.",
            "Codes, Einheiten, Skalierung und Meldezeit vor dem Einbau auswerten."])
d.merksatz("Aus ist nicht offline. Ein lesbarer alter Cloud-Wert beweist nicht, dass das Gerät gerade erreichbar ist.")
d.h1("3  Letzte Messung der Waage")
d.p("Eine spätere Waagenansicht darf nur belegte Werte anzeigen. Ein fehlendes Feld bleibt leer. Es wird weder ein Körperwert erfunden noch aus anderen Werten berechnet. Die Rohzahl muss mit der dokumentierten Einheit und Skalierung des konkreten Geräts verbunden werden.")
d.merksatz("Die Meldezeit einer Eigenschaft ist nicht automatisch die Wiegezeit. Ein neuer Akkuwert darf keine alte Gewichtsmessung als frisch erscheinen lassen.", warn=True)
d.h2("4  Prüfgrenzen und Datenschutz")
d.p("Ob die Cloud alle Bluetooth-Werte der Handy-App liefert, ist noch offen. Zugangsdaten, lokale Schlüssel und Geräte-IDs werden nicht ausgegeben. Persönliche Messwerte können in der Ausgabe stehen.")
d.p("Quellen: Tuya-Geräteinformation, Gerätespezifikation und Query Properties. Links: docs/TUYA_DIAGNOSE_UND_WAAGE.md.")
d.save("docs/Lernunterlage-Tuya-Diagnose-und-Waage.pdf")
