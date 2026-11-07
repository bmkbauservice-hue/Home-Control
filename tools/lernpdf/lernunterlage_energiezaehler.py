from lernpdf import Doc

d = Doc("Energie in Home Control", "Watt, Kilowattstunden und Messlücken", footer="Home Control - Energiezähler")
d.h1("Was zeigt die Übersicht?")
d.p("Licht, Steckdosen und Schalter erscheinen als Netzgeräte. Temperatur- und Türsensoren bleiben in der Energieübersicht außen vor. Eine Steckdose kann schaltbar sein, ohne Wattwerte zu liefern.")
d.h1("Watt und Kilowattstunden")
d.p("Watt ist die Leistung zum Zeitpunkt des Abrufs. Kilowattstunden beschreiben den Verbrauch über eine Zeit. 100 Watt für eine halbe Stunde ergeben 0,050 kWh.")
d.h1("Wie entsteht die Schätzung?")
d.p("Das Backend mittelt zwei aufeinanderfolgende Wattwerte eines online gemeldeten Geräts. Durchschnittliche Watt mal Stunden geteilt durch 1.000 ergeben geschätzte kWh. Es addiert diese zur gespeicherten Summe des Geräts.")
d.p("Fehlende und offline gemeldete Proben unterbrechen die Folge. Bei mehr als 35 Minuten Abstand wird für dieses Intervall nichts addiert. Kurze Verbrauchsspitzen zwischen zwei Abfragen können fehlen.")
d.h1("Was kann ich prüfen?")
d.p("Die Geräteübersicht nennt fehlende oder alte Werte. Nach zwei gültigen Abgleichen erscheint die erste kWh-Schätzung. Im lokalen Test ist der automatische Tuya-Abgleich aus. Ein Abgleich lässt sich auf der Geräteseite auslösen.")
d.save("docs/Lernunterlage-Energiezaehler.pdf")
