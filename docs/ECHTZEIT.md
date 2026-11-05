# Echtzeit: Vom Gerät bis ins Dashboard

**Zum Lernen:** [Lernunterlage-Echtzeit-und-API.pdf](Lernunterlage-Echtzeit-und-API.pdf)

Ziel: Ändert sich ein Tuya-Gerät, soll das Dashboard es möglichst sofort zeigen, ohne die Seite neu zu laden.
Der Weg hat **zwei Abschnitte**, die man getrennt betrachten muss:

```text
Gerät ──► Tuya-Cloud ══ Abschnitt B ══► Backend ══ Abschnitt A ══► Browser
                        heute: Polling            neu: SSE, sofort
                        (Tür 3 min, Rest 30 min)
```

**Ehrlicher Stand:** Abschnitt A ist gebaut und getestet. Abschnitt B ist weiterhin **Polling**. Im echten Betrieb
erscheint eine Türöffnung also spätestens nach dem nächsten Abgleich (bis zu 3 Minuten), dann aber sofort
im Browser. **Eine echte Tuya-Echtzeit gibt es noch nicht.** Der Chip „Live“ meint nur Abschnitt A, das steht auch
in seinem Tooltip.

## Abschnitt A: Backend → Browser (umgesetzt)

### Warum SSE und nicht SignalR?

| Kriterium | Server-Sent Events (gewählt) | SignalR |
|---|---|---|
| Richtung | Server → Browser, genau unser Fall | beide Richtungen, hier nicht gebraucht |
| Abhängigkeiten | keine: `TypedResults.ServerSentEvents` ist in .NET 10 eingebaut, im Browser reicht `fetch` | eigene Client-Bibliothek |
| Anmeldung | Token wie immer im Header, weil wir `fetch` statt `EventSource` benutzen | im Browser meist als `access_token` in der URL (WebSockets können keine Header setzen) |
| Technik | normales HTTP, geht durch `tailscale serve`, Vite-Proxy und Docker-Portweiterleitung | braucht WebSocket oder fällt zurück |
| Wiederverbinden | selbst geschrieben, etwa 10 Zeilen | eingebaut |
| Erklärbarkeit | „eine HTTP-Antwort, die nie aufhört“ | Hub, Gruppen, Protokollaushandlung |

SignalR lohnt sich, wenn der Browser später über denselben Kanal auch sendet. Für die spätere Gerätesteuerung ist
das nicht nötig: Ein Schaltbefehl ist ein normaler `POST`.

### So funktioniert es

```text
TuyaDeviceSyncer speichert ─► DeviceChangeHub.Publish ─► je Verbindung eine Warteschlange
                                                                │
Browser ◄── event: device-change ── GET /api/live/devices ◄─────┘
   │
   └─► lädt über die normalen Endpunkte neu (GET /api/devices, GET /api/status)
```

| Baustein | Aufgabe |
|---|---|
| `Realtime/DeviceChangeHub.cs` | Verteilstelle im Arbeitsspeicher (Publish/Subscribe). Pro Verbindung ein `Channel` mit 32 Plätzen; ist er voll, fällt die älteste Meldung weg. Höchstens 100 Verbindungen. |
| `Endpoints/RealtimeEndpoints.cs` | `GET /api/live/devices`: `connected`, dann `device-change`, alle 25 s `ping` |
| `Tuya/TuyaDeviceSyncer.cs` | meldet nach dem Speichern, welche Geräte sich wirklich geändert haben (EF Core `ChangeTracker`, dazu Wechsel des Online-Zustands). Kein Wechsel, keine Meldung. |
| `frontend/src/api/liveEvents.ts` | liest den Strom mit `fetch`, verbindet neu, trennt bei verstecktem Tab |
| `frontend/src/hooks/useLiveRefresh.ts` | lädt die Seite nach einer Meldung neu, 300 ms gesammelt |

### Entscheidungen

- **Nur Ids, keine Werte im Ereignis.** Der Browser holt die Daten über die normalen Endpunkte. Es gibt also nur
  einen Weg zu den Daten, mit denselben Rechten, und eine verlorene Meldung schadet nicht: Die nächste holt alles.
- **Erster Datenabruf bleibt REST.** Die Seite lädt wie bisher. Der Kanal sagt nur „es gibt etwas Neues“.
- **Nach einer Unterbrechung einmal alles neu laden** (das `connected`-Ereignis). So braucht es keinen Puffer
  für verpasste Meldungen und kein `Last-Event-ID`.
- **Polling bleibt als Rückfallebene.** Alle Seiten laden weiter jede Minute neu. Fällt der Kanal aus, zeigt der Chip
  „Live aus“, sonst ändert sich nichts.

### Sicherheit

- Nur mit JWT (Policy `SignedInUser`), der Agenten-Schlüssel bekommt 401.
- **Der Strom endet, wenn das Access Token abläuft** (höchstens 15 Minuten). Die Anmeldung wird nur beim Aufbau
  geprüft, sonst könnte jemand mit einem abgelaufenen Token ewig weiterlauschen. Der Browser verbindet sich neu; ein
  abgelaufenes Token bekommt 401, `send()` holt per Refresh ein neues. Ist die Sitzung beendet, gibt es keins mehr.
- Das Token steht im Header, nie in der URL (und damit nie in Logs).
- Das Ereignis enthält keine Messwerte, nur Geräte-Ids.

### Nachweis

Automatisch (`RealtimeTests`, ohne echtes Tuya): Verteilung, volle Warteschlange, Obergrenze, Reihenfolge, Lebenszeichen,
Ende beim Tokenablauf, Browser trennt, nur echte Änderungen werden gemeldet, 401 ohne JWT und mit Agenten-Schlüssel,
der ganze Weg im Demo-Modus.

Von Hand im echten Browser (Chromium, Demo-Modus, 05.11.2026):

| Prüfung | Ergebnis |
|---|---|
| Simulierte Tür, Übersicht ohne Neuladen | 3 von 3 Änderungen sichtbar nach 0,32 s, Seite nicht neu geladen |
| Geräteseite | sichtbar nach 0,32 s |
| Server beenden mit offenem Strom | beendet nach 0,1 s, Chip „Live aus“ |
| Server neu starten (neuer JWT-Schlüssel) | verbindet sich selbst neu, bleibt angemeldet, nächste Änderung sofort sichtbar |
| Tab verstecken und zurückholen | Verbindung zu, danach wieder auf |

**Nicht geprüft:** echte Tuya-Geräte über den Kanal, `tailscale serve` und Safari auf dem iPhone. Das geht erst nach
dem Merge im Dienst.

### Selbst ausprobieren

```bash
./demo-starten.sh
```

In einem zweiten Terminal (Passwort und Token bleiben nur in der Variable):

```bash
TOKEN=$(curl -s http://localhost:5300/api/auth/login -H 'Content-Type: application/json' -d '{"email":"demo@example.de","password":"Demo-Haus-2026"}' | python3 -c 'import json,sys; print(json.load(sys.stdin)["accessToken"])')
```

```bash
curl -i -X POST http://localhost:5300/api/demo/simulation/tuer -H "Authorization: Bearer $TOKEN"
```

Die Haustür im offenen Browser wechselt sofort. Den Strom selbst kann man sich so ansehen:

```bash
curl -N http://localhost:5300/api/live/devices -H "Authorization: Bearer $TOKEN"
```

`POST /api/demo/simulation/tuer` gibt es nur im Demo-Modus (sonst 404) und nur für Admins.

### Grenzen

- **Nur der Abgleich meldet.** Ändert ein anderer Admin ein Gerät von Hand, sieht man das erst beim nächsten Minutentakt.
- **Ein Prozess:** Die Verteilstelle liegt im Arbeitsspeicher. Mehrere Backend-Instanzen bräuchten etwas wie Redis.
- **HTTP/1.1 am Rechner:** Ein Browser hält pro Adresse nur etwa 6 Verbindungen. Deshalb trennt ein versteckter Tab.
  Über HTTPS (`tailscale serve`, HTTP/2) ist die Grenze deutlich höher.

## Abschnitt B: Tuya-Cloud → Backend (recherchiert, nicht gebaut)

Stand der Recherche: 05.11.2026, Tuya-Dokumentation. **Nichts davon ist an deinem Konto getestet.**

| Frage | Stand laut Doku |
|---|---|
| Technik | Tuya **Message Service**, derzeit nur „Message Queue“ auf Basis von Apache Pulsar. Europa: `pulsar+ssl://mqe.tuyaeu.com:7285/` |
| Einrichtung | Portal → Cloud → Message Service → Projekt wählen und einschalten. Wird nach etwa 30 Minuten wirksam, dann Status „Subscribed“. Es gibt ein Testgerät mit Testkanal. |
| Ereignisse | u. a. `dp_report` (Status mit `code`, `value`, `time` je Eintrag), `online`, `offline`, `bind`, `unbind`, `reset`, `name_update` |
| Client für .NET | offizielles [tuya-pulsar-sdk-dotnet](https://developer.tuya.com/en/docs/iot/Pulsar-SDK-get-message-c?id=Kawpkk5vic1es), Alternative Apache DotPulsar |
| Verbindung | **ausgehend** vom Backend, kein offener Port und keine öffentliche Adresse nötig. Auch im Docker-Container oder hinter Tailscale. |
| Sicherheit | TLS, Inhalt zusätzlich AES-128 verschlüsselt und signiert, Anmeldung mit Access ID und Access Secret. Das Secret bleibt im Backend. |
| Verpasste Nachrichten | Pulsar hebt Nachrichten bis zur Bestätigung (Ack) auf, auch wenn das Backend offline ist. Eine feste Aufbewahrungsdauer nennt die Doku nicht. |
| Reihenfolge | keine Garantie genannt, ein Topic hat mehrere Partitionen. Pro Eintrag gibt es aber ein `time`, damit lassen sich veraltete Meldungen erkennen. Wiederholte Zustellung ist möglich (`dataId` hilft beim Erkennen). |
| Kosten | laut Doku Abrechnung nach weitergeleiteten Nachrichten. **Die Zahlen sind ungeklärt:** TUYA.md nennt 68 000 pro Monat von der Produktseite, eine Drittquelle 140 000. Maßgeblich ist das Portal („My Services“). Man bekommt alle Meldungen, Steckdosen mit Strommessung könnten viele senden. |

### Offene Risiken

1. **Gerätekompatibilität ist nicht belegt.** Die Doku schreibt, für Gerätenachrichten sei ein Client
   „through the OEM app or App SDK“ zu entwickeln. Unsere Geräte hängen über ein verknüpftes App-Konto (Smart Life) am Cloud-Projekt.
   Ob sie Nachrichten liefern, steht dort nicht. Der erste Schritt eines Spikes ist genau diese Frage.
2. **Akku-Sensoren** (Tür) melden sich nur beim Aufwachen. Das passt gut zu Push, muss aber gemessen werden.
3. Eine Nachricht **ohne Bestätigung** wird erneut zugestellt. Die Verarbeitung muss das aushalten (Idempotenz).

### Vorschlag für den Spike (nach der Präsentation)

Konsolenprogramm wie `tools/BlinkSpike`, ohne Änderung am Dienst:

1. Im Portal prüfen: Message Service aktiv, Kontingent, Testgerät.
2. Verbinden, nur mitlesen und Anzahl sowie Art der Nachrichten eine Woche protokollieren. **Ohne Bestätigung** im
   Produktivkanal nichts wegnehmen, sondern zuerst den Testkanal benutzen.
3. Erst wenn das klappt: Hintergrunddienst `TuyaPushService`, der `dp_report` auf den vorhandenen
   `TuyaDeviceSyncer.Apply` gibt und dann `DeviceChangeHub.Publish` aufruft (neue Quelle in `DeviceChangeSources`).
   **Abschnitt A bleibt unverändert.**

### Zusammenspiel mit dem Polling

Das Polling bleibt in jedem Fall als Rückfallebene, nur seltener (z. B. Tür 30 statt 3 Minuten):

- `Apply` ist schon heute unempfindlich gegen doppelte Meldungen: Gleicher Wert, kein Ereignis.
- Fällt die Pulsar-Verbindung aus, gleicht der nächste Abgleich die Lücke aus.
- Vor dem Übernehmen muss `time` der Meldung mit `MeasuredAtUtc` verglichen werden, sonst überschreibt eine
  verspätete Meldung einen neueren Stand. Das kann `Apply` heute noch nicht.

Quellen: [Device Message Subscription](https://developer.tuya.com/en/docs/iot/subscribe?id=Kbwtw7fhhjabw),
[Enable Message Service](https://developer.tuya.com/en/docs/iot/subscribe-mq?id=Kavqcrvckbh9h),
[Pulsar C# SDK](https://developer.tuya.com/en/docs/iot/Pulsar-SDK-get-message-c?id=Kawpkk5vic1es),
[TypedResults.ServerSentEvents](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.http.typedresults.serversentevents?view=aspnetcore-10.0).
