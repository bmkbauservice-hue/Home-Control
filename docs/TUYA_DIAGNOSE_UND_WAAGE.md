# Tuya-Lampen und Silvergear Smart Scale prüfen

Stand: 06.11.2026. Gemeinsamer Teststand bleibt `Home-Control-Gemeinsam` auf Port 5181.

## Bekannter Stand

- Nutzer bestätigt: Heizdecke lässt sich schalten. Alle vier Klimasensoren sind jetzt in Home Control sichtbar und wurden aktualisiert.
- Badlicht ist eine schaltbare Steckdose für den Spiegelschrank. Bar Licht und Stehlampe im Wohnzimmer sollen ebenfalls schaltbar sein. Die Offline-Anzeige ist noch zu klären.
- Gelesene Tuya-Rohwerte für Bar Licht und Stehlampe enthalten `switch_1` als Boolean; ein anderer Schaltcode ist für diese beiden Geräte nicht belegt.
- Die Silvergear Smart Scale erscheint in der Tuya-Auswahl. Gewünscht ist eine eigene Ansicht der letzten Messung mit allen tatsächlich abrufbaren Werten.
- Feldnamen, Einheiten und Messzeit der konkreten Waage sind noch nicht bekannt. Eine fertige Waagenansicht oder vollständiger Cloud-Zugriff wird noch nicht behauptet.

## Ein gemeinsamer, lesender Diagnoseaufruf

```bash
dotnet run --project tools/TuyaDiagnose
```

Das Werkzeug verwendet die vorhandenen lokalen .NET User Secrets. Kein Passwort erneut eingeben
und keine Zugangsdaten teilen. Es startet keinen Webserver, öffnet keine Datenbank und ändert
keinen laufenden Dienst. Die Sensor-Bridge kann weiterlaufen.

Zuerst werden die Cloud-Gerätenamen, Produkte, Kategorien und der Online-Status der App-Liste
angezeigt. Dann die Nummern für Badlicht, Bar Licht, Stehlampe und die Silvergear-Waage eingeben,
mit Komma getrennt. Erst nach einer gültigen Auswahl folgen die weiteren Abrufe.

Für jedes gewählte Gerät liest das Werkzeug:

1. Direkte Geräteinformation: Online-Status unabhängig von der App-Geräteliste.
2. Bisherige Statuswerte: vorhandene Datenpunkt-Codes und unveränderte Rohwerte.
3. Gerätespezifikation: unterstützte Codes, Datentypen, Einheiten und Skalierung.
4. Letzte Cloud-Eigenschaftswerte mit deren Meldezeit, soweit die Cloud-Berechtigungen das erlauben.

Es werden keine Tuya-Schaltbefehle gesendet. Geräte-IDs, lokale Geräteschlüssel und Zugangsdaten
werden nicht ausgegeben. Die Ausgabe kann persönliche Waagenmesswerte enthalten und wird
nicht automatisch gespeichert. Tuya-Fehler werden als Fehlercode angezeigt, ohne daraus
einen Geräte- oder Messzustand zu erfinden. Jeder Abruf verbraucht Cloud-Kontingent.

## Auswertung der Lampen

`offline` und `aus` sind unterschiedliche Angaben. Ein erfolgreicher Rohwertabruf kann auch
einen alten Cloud-Stand liefern. Stimmen direkte Geräteinformation und App-Geräteliste nicht
überein, ist genau dieser Unterschied zu untersuchen. Die Online-Sperre im Schaltendpunkt
wird für die Diagnose nicht aufgehoben.

Eine erfolgreiche Bedienung in der Tuya-Handy-App und anschließend ein manueller Abgleich
auf der Home-Control-Geräteseite helfen beim Vergleich. Der automatische Abgleich ist in der
aktuellen Testinstanz bewusst aus. Eine Status-LED allein beweist keine physische Schaltwirkung.

## Grundlage für die Waagenansicht

Die Anzeige soll die letzte tatsächliche Wiegung und die verfügbaren Werte zeigen, einschließlich
Einheit und Zeitpunkt. Gewicht, Fettanteile oder andere Größen werden erst zugeordnet, wenn
die konkreten Datenpunkte und deren Definitionen gelesen wurden. Die Meldezeit einer Eigenschaft
ist nicht automatisch die Wiegezeit; etwa ein Akkuwert darf eine alte Wiegung nicht frisch aussehen lassen.

Ob die Cloud alle Werte der Bluetooth-Anzeige im Handy liefert, ist noch zu prüfen. Ein leeres
Feld oder fehlende Berechtigung wird benannt. Es werden keine Gesundheitswerte berechnet,
ergänzt oder bewertet. Die Oberfläche bleibt hinter der Home-Control-Anmeldung.

## Quellen

- [Geräteinformation](https://developer.tuya.com/en/docs/cloud/7d3f13ae55?id=Kb2rzcwpmvaci)
- [Gerätespezifikation](https://developer.tuya.com/en/docs/cloud/68c2e82f73?id=Kag2ybtxwlb9w)
- [Letzte Eigenschaftswerte mit Zeitstempel](https://developer.tuya.com/en/docs/cloud/116cc8bf6f?id=Kcp2kwfrpe719)
- [Lernunterlage](Lernunterlage-Tuya-Diagnose-und-Waage.pdf)


## Vorhandenes Zigbee-Gateway prüfen

Im vorhandenen Projektordner:

```bash
dotnet run --project tools/TuyaDiagnose -- --gateway
```

Die virtuelle Geräte-ID des **Gateways** aus Lidl Home am Terminal eingeben. Das Werkzeug
fragt mit den bestehenden Projektzugängen den dokumentierten [Tuya-Endpunkt für Untergeräte](https://developer.tuya.com/en/docs/cloud/ac516a1e3a?id=Kag2y1eyp1xck)
ab. Es zeigt Namen, Kategorie und Online-Status. Die ID erscheint nicht in der Ergebnisliste.

Ein erfolgreicher Abruf zeigt, welche Untergeräte das Projekt über die Cloud sieht.
Bei einer Ablehnung kommt nur der Tuya-Fehlercode. Aus einer Ablehnung lässt sich
keine Aussage über das Zigbee-Funknetz ableiten. Es werden weder Geräte geschaltet
noch Kopplungen oder die Home-Control-Datenbank geändert.
