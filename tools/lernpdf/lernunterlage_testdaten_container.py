from lernpdf import BODY, Doc

BODY.fontSize = 9.25
BODY.leading = 12.5
d = Doc("Testdaten und Container", "Home Control - SQLite-Sicherung, Wiederaufnahme und Sensor-Gateway",
        footer="Home Control - Testdaten und Container - Stand 06.11.2026")
d.h1("1  Warum dieselbe Sitzung?")
d.p("Ein neuer Import der alten Hausdaten enthält später hinzugefügte Geräte und Sensorzuordnungen nicht. Der Teststarter merkt sich deshalb die aktiven Haus- und Klima-Dateien in einer privaten session.json. Ein normaler Neustart verwendet genau diese Dateien weiter.")
d.code("python3 tools/live-mit-hausdaten-starten.py --nur-sichern")
d.p("Zuerst bei noch laufender Test-API ausführen. Nur der eigene Prozess im gemeinsamen Projektordner auf Port 5181 darf übernommen werden. Beide Datenbankpfade müssen im eigenen Testordner liegen. Der Dauerdienst wird nicht gestoppt.")
d.h1("2  SQLite und WAL")
d.p("SQLite kann bestätigte Änderungen zunächst in einer WAL-Datei speichern. Eine einfache Kopie nur der .db-Datei kann dann unvollständig sein. Die SQLite-Backup-API erstellt eine konsistente Kopie einschließlich dieser Änderungen. Haus- und Klima-Datei werden separat gesichert, nicht in einer gemeinsamen Transaktion.")
d.merksatz("Neustart setzt fort. --neu importiert bewusst erneut. Eine defekte Sitzung wird nicht automatisch durch alte Daten ersetzt.")
d.h1("3  Der Sensorweg im Container")
d.table([["Teil", "Aufgabe"], ["Android-App", "Sendet Messwerte mit privatem Bridge-Schlüssel über USB."],
         ["Host-Port 5391", "Optionaler Docker-Sensorzugang, nur auf Loopback veröffentlicht."],
         ["Sensor-Gateway", "Teilt den Netzwerkraum der API und leitet intern an 127.0.0.1:8080 weiter."],
         ["Daten-Volume", "Erhält Haus- und Klima-SQLite über neue Container hinweg."]], [0.3, 0.7])
d.p("GitHub Actions baut das Image und prüft mit ausschließlich synthetischen Wegwerfdaten Anmeldung, Schlüsselprüfung, Gateway und Datenerhaltung. Dieser Test ersetzt weder den echten Hardwaretest noch den späteren Datenumzug.")
d.h1("4  Blink und Grenzen")
d.p("Das Blink-Refresh-Token rotiert. Nur eine Instanz darf es erneuern. Vor der Übernahme müssen die aktuelle tokenführende Datenbank und ihre Data-Protection-Schlüssel erhalten werden. Im parallelen Test bleibt Blink deaktiviert. Eine alte Testkopie ist kein Nachweis für ein noch gültiges Token.")
d.p("Lokal fehlen hier .NET SDK und Docker. Die Gesamtprüfung läuft zusätzlich in GitHub CI. Auf dem Nutzer-PC wurde kein Dienst umgestellt. Details und Quellen: docs/TESTDATEN_UND_CONTAINER.md.")
d.save("docs/Lernunterlage-Testdaten-und-Container.pdf")
