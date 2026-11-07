# Waage: letzte Cloud-Werte

Die neue Seite **Waage** ist ausschließlich für Admins sichtbar. Auch bei ansonsten offenem
Lesemodus verlangt das Backend die Admin-Rolle. Antworten tragen `Cache-Control: no-store`.
Die Demo liest weder die private Zuordnung noch echte Waagendaten.

## Bestätigt und offen

Der Nutzer hat eine Silvergear Smart Scale, deren Bluetooth-Werte in der Tuya-Handy-App sichtbar sind.
Die bisherige Cloud-Diagnose listet die Waage nicht auf. Deshalb gibt es noch keinen bestätigten
Gewichtscode, keine bestätigte Einheit und keinen Hardwaretest für diese Ansicht.
Die Seite zeigt in diesem Zustand **keine Messung** und erklärt den fehlenden Cloud-Zugriff.
Sie erweitert nicht die Google-Home-Temperatur-Bridge und liest nicht direkt Bluetooth aus.

## Sobald die Waage in der Cloud bereitsteht

1. Als Admin die Seite „Waage“ öffnen und „Waage auswählen“ drücken.
2. Nur die tatsächliche Waage aus der Tuya-Geräteliste verbinden. Fehlt sie, hier abbrechen;
   eine Steckdose ist kein Ersatz. Direkt eingetragene fremde Geräte-IDs werden vom Backend abgelehnt.
3. Die gemeldeten Werte kontrollieren. Datenpunkte, die Tuya nicht bereitstellt, können nicht angezeigt werden.
4. Das tatsächlich als Gewicht erkannte Zahlenfeld auswählen und „Gewichtsfeld speichern“ drücken.
   Eine automatische Ableitung aus dem Namen oder einer geratenen Liste von Codes findet nicht statt.
5. „Aktualisieren“ fragt den letzten Cloud-Stand ab, höchstens einmal pro Minute. Es löst keine Wiegung aus.

Auch bei einer derzeit offline gemeldeten Waage können gespeicherte Cloud-Eigenschaften gelesen werden.
Das ist keine Live-Verbindung zum Bluetooth-Gerät. Die Online-Prüfung für Tuya-Schaltbefehle bleibt bestehen.

## Welche Zeiten und Werte angezeigt werden

- Die Gewichtskarte verwendet ausschließlich den Meldezeitpunkt des ausgewählten Gewichtsfelds.
- Neuere Akkuwerte ändern diesen Zeitpunkt nicht. Fehlende Zeitstempel bleiben unbekannt.
- Der Cloud-Abrufzeitpunkt wird separat beschriftet. Er ist weder Mess- noch Meldezeit.
- Rohwerte bleiben zusätzlich sichtbar. Eine Division durch `10 ** scale` erfolgt nur bei einer Zahl
  und einem von der Felddefinition gelieferten ganzzahligen Faktor zwischen 0 und 9.
- Ohne brauchbare Definition bleibt die Anzeige ein Rohwert. Eine Einheit oder Skalierung wird nicht geraten.
- Es werden keine weiteren Körperwerte berechnet und keine medizinischen Bewertungen abgeleitet.

Die Antwort stammt aus Tuyas letzten Cloud-Eigenschaften (`shadow/properties`). Der konkrete Umfang hängt
vom Gerät, dessen Übertragung und der API-Freigabe ab. Dass die Tuya-App auf dem Handy weitere Werte zeigt,
belegt noch nicht deren Verfügbarkeit über diese Cloud-Schnittstelle.

## Speicherung und Schutz

`Scale:SettingsPath` bezeichnet eine private JSON-Datei mit Geräte-ID, Anzeigename und Gewichtscode.
Der Standard liegt außerhalb des Repos unter `~/.local/share/HomeControl/scale-settings.json`.
Die Datei wird atomar ersetzt und unter Linux mit Modus 0600 erzeugt. **Körperwerte werden nicht darin gespeichert.**
Im Teststarter liegt die Zuordnung unter `test-snapshots/scale-settings.json` und wird mitgesichert.
Im Container liegt sie unter `/data/scale-settings.json` im dauerhaften Daten-Volume.
„Zuordnung entfernen“ entfernt nur die lokale Verknüpfung; es löscht weder das Tuya-Gerät noch dessen Messungen.

## Prüfung

Die Backend-Tests ersetzen Tuya vollständig: Admin-Schutz, unverknüpfte Geräte, letzte Werte auch offline,
separate Zeitstempel, Rohwerte bei fehlender Felddefinition, Wiederverwendung des Caches,
Demo-Sperre und private Zuordnungsdatei. Die Tests enthalten ausschließlich synthetische Werte.
Node-Tests prüfen deutsche Zahlenformatierung ohne geratene Skalierung.
Der Frontend-Build prüft TypeScript. Ein echter Waagenabruf ist damit nicht nachgewiesen.

## Tuya-Quellen

- Eigenschaften: https://developer.tuya.com/en/docs/cloud/116cc8bf6f?id=Kcp2kwfrpe719
- Felddefinitionen: https://developer.tuya.com/en/docs/cloud/68c2e82f73?id=Kag2ybtxwlb9w
