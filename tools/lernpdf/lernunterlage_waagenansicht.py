from lernpdf import BODY, Doc

BODY.fontSize = 9.25
BODY.leading = 12.5
d = Doc("Die Waagenansicht", "Home Control - tatsächliche Cloud-Werte, Felddefinitionen und Datenschutz",
        footer="Home Control - Waagenansicht - Stand 06.11.2026")
d.h1("1  Was ist umgesetzt?")
d.p("Eine eigene Admin-Seite zeigt die letzten an Tuya gemeldeten Eigenschaften einer zugeordneten Waage. Der Nutzer wählt das tatsächliche Gerät und das tatsächliche Gewichtsfeld ausdrücklich aus. Zusatzwerte werden mit Rohwert und eigener Meldezeit angezeigt. Das Backend sendet ausschließlich lesende Tuya-Anfragen.")
d.merksatz("Die Silvergear ist bisher nicht in der Cloud-Geräteliste enthalten. Ohne echte Daten zeigt die Seite keine Messung. Sichtbarkeit per Bluetooth auf dem Handy belegt keinen Cloud-Zugriff.", warn=True)
d.h1("2  Rohwert und Skalierung")
d.p("Manche Geräte melden Zahlen als Ganzzahl und liefern die Dezimalstellen separat in ihrer Felddefinition: Anzeigewert = Rohwert / 10 hoch scale. Nur eine tatsächlich gelieferte Definition darf diese Umrechnung bestimmen. Fehlt sie, bleibt die Zahl ausdrücklich ein Rohwert. Es wird keine Einheit geraten.")
d.table([["Zeitangabe", "Bedeutung"], ["Meldezeit Gewicht", "Wann Tuya das ausgewählte Gewichtsfeld zuletzt empfangen hat."],
         ["Meldezeit Akku", "Gehört nur zum Akku. Macht das Gewicht nicht neuer."],
         ["Cloud-Abruf", "Wann unser Server die gespeicherten Eigenschaften gelesen hat."],
         ["Physische Wiegezeit", "Nicht automatisch durch einen dieser Zeitpunkte belegt."]], [0.32, 0.68])
d.h1("3  Zugriff und Speicherung")
d.p("Die API verlangt die Admin-Rolle, auch wenn andere Hausdaten ohne Anmeldung gelesen werden dürfen. no-store verhindert Browser-Caching. Serverseitig werden Abrufe 60 Sekunden wiederverwendet. Die Demo lädt keine privaten Waagendaten.")
d.p("Dauerhaft gespeichert wird nur die Zuordnung: Geräte-ID, Name und Gewichtscode. Eine private JSON-Datei wird atomar ersetzt und unter Linux mit Modus 0600 angelegt. Körperwerte werden nicht lokal gespeichert. Das Docker-Daten-Volume erhält die Zuordnung über Container-Neuerstellungen hinweg.")
d.h1("4  Was beweisen die Tests?")
d.p("Die Tests verwenden synthetische Daten und prüfen Rechte, Feldzeiten, fehlende Definitionen, ungültige Geräte, Cache und private Speicherung. Sie belegen keinen Zugriff auf die echte Bluetooth-Waage. Für diesen Nachweis muss das tatsächliche Gerät mit seinen tatsächlichen Datenpunkten verfügbar sein.")
d.p("Bedienung, Code-Landkarte und Tuya-Quellen: docs/WAAGE.md. Keine medizinische Bewertung und keine Berechnung fehlender Körperwerte.")
d.save("docs/Lernunterlage-Waagenansicht.pdf")
