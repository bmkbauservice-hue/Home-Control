from lernpdf import Doc

d = Doc("Android-Sensor-Bridge", "Home Control - bestehende App verbinden, Stand 06.11.2026",
        footer="Home Control - Sensor-Bridge - Lernunterlage")
d.h1("1  Was fehlt bisher?")
d.p("Die Android-Test-App kann Temperatur-Traits auswerten. Im geprüften lokalen Quellcode war aber kein Aufruf des Senders zu Home Control vorhanden. Ein Bridge-Schlüssel allein stellt keine Datenverbindung her.")
d.table([['Teil', 'Aufgabe'], ['Android-App', 'Echte Temperatur und Luftfeuchte vom Home SDK lesen'],
         ['USB und Gateway', 'Messwerte an das lokale Backend weiterleiten'],
         ['Backend', 'Schlüssel und Messwerte prüfen, getrennte Klima-SQLite speichern'],
         ['Klima-Seite', 'Sensoren zuordnen, Werte und Empfangszeit anzeigen']], [0.27, 0.73])
d.h1("2  Was ergänzt der Einbau?")
d.bullets(["Sender am vorhandenen SDK-Geräteereignis aufrufen; Temperatur und Luftfeuchte registrieren.",
           "Quellcode vorab prüfen und betroffene Dateien sichern; bei abweichenden Dateien anhalten.",
           "Schlüssel lokal halten; per Build-Umgebung an die Test-App übergeben.",
           "Bestehende App über USB aktualisieren und einen Gateway nur auf localhost starten."])
d.merksatz("Keine erfundenen Messwerte: Fehlt ein Wert, bleibt er leer. Der Prüfaufruf enthält gar keine Messwerte und darf nichts speichern.")
d.h1("3  Wie wird geprüft?")
d.numbered(["Gemeinsame Testversion auf Port 5181 laufen lassen und Android per USB anschließen.",
            "Einbauskript erst ohne --install aufrufen: Es prüft nur den vorhandenen Quellcode.",
            "Mit --install sichern, bauen und installieren. Bei Fehlern Ausgabe prüfen.",
            "Test-App im Vordergrund öffnen, Haus wählen und echten Empfang im Klima-Menü prüfen.",
            "Die vier Sensoren Wohnzimmer, Gästezimmer, Schlafzimmer und Bad zuordnen."])
d.merksatz("Python-Tests prüfen den Einbau. Sie beweisen weder einen erfolgreichen Android-Build noch echte Sensorübertragung. Beides muss auf dem Linux-PC geprüft werden.", warn=True)
d.page_break()
d.h1("4  Grenzen des ersten Tests")
d.p("USB-Verbindung, Android-App im Vordergrund und Gateway-Terminal müssen offen bleiben. Dies ist noch kein Hintergrunddienst. Der Sender reagiert auf SDK-Ereignisse; alte Werte werden nicht als neue Messungen wiederholt.")
d.p("Der Zeitstempel im Backend beschreibt den Empfang. Er belegt nicht den Zeitpunkt der Messung am physischen Sensor. Nicht vorhandene Luftfeuchte wird nicht aus einer Temperatur berechnet.")
d.h1("5  Docker und Blink")
d.p("Docker besitzt bereits ein Daten-Volume. Die neue Klima-Datenbank muss ebenfalls unter /data liegen. Die Demo bekommt eine eigene Datei, damit echte und simulierte Messwerte getrennt bleiben. Der Sensor-Gateway-Weg in den Container ist noch gesondert zu prüfen.")
d.p("Blink ist im Projekt vorhanden, im Test aber ausgeschaltet. Vor einer Übernahme muss klar sein, welche laufende Instanz das Blink-Token erneuert. Token und Data-Protection-Schlüssel müssen gemeinsam erhalten bleiben.")
d.h1("6  Was muss ich erklären können?")
d.bullets(["Ein Trait beschreibt eine Geräteeigenschaft. Die Bridge liest Temperatur und Feuchte, keine geratenen Zahlen.",
           "Ein Gateway verbindet zwei Schnittstellen. Hier bleibt das Backend lokal erreichbar.",
           "Ein Volume erhält Daten beim Austausch eines Containers.",
           "Eine Sicherung und ein Dry-Run ermöglichen eine überprüfbare Änderung an vorhandenen Dateien."])
d.p("Quellen: developers.home.google.com/apis/android/device und /data-model; Google-Referenz für TemperatureMeasurement und RelativeHumidityMeasurement. Vollständige Links: docs/ANDROID_SENSOR_BRIDGE.md.")
d.save("docs/Lernunterlage-Android-Sensor-Bridge.pdf")
