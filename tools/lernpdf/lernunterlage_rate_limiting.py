"""Erzeugt docs/Lernunterlage-Rate-Limiting.pdf (Aufruf aus dem Repo-Hauptordner:
PYTHONPATH=tools/lernpdf python3 tools/lernpdf/lernunterlage_rate_limiting.py)."""
from lernpdf import Doc

M = "<font name='Mono'>"
E = "</font>"

d = Doc(
    "Rate Limiting",
    "Lernunterlage: Anfragen begrenzen mit dem eingebauten Rate Limiter von ASP.NET Core",
    footer="Home Control · Rate Limiting · Lernunterlage",
)

# ------------------------------------------------------------------ 1
d.h1("1  Worum es geht")
d.p("<b>Rate Limiting</b> begrenzt, wie viele Anfragen in einer bestimmten Zeit erlaubt sind, zum Beispiel "
    "„höchstens 10 Anmeldeversuche pro Minute“. Wer mehr schickt, bekommt den Statuscode <b>429 Too Many "
    "Requests</b>, und der Endpunkt läuft gar nicht erst.")
d.p("Das Security-Review (REVIEW.md) hatte zwei Lücken gefunden:")
d.bullets([
    "<b>M-2:</b> Jeder Aufruf unter " + M + "/api/tuya" + E + " fragt die Tuya-Cloud. Eine Schleife könnte "
    "das Kontingent des Tuya-Projekts aufbrauchen. Danach gehen Status-LEDs und Abgleich nicht mehr.",
    "<b>M-3:</b> Gegen Passwort-Raten schützte nur die Kontosperre (5 Fehlversuche, 5 Minuten). Viele Konten "
    "nacheinander durchzuprobieren, war ungebremst möglich.",
])

# ------------------------------------------------------------------ 2
d.h1("2  Die Verfahren im Vergleich")
d.p("Der Rate Limiter von ASP.NET Core (Namespace " + M + "System.Threading.RateLimiting" + E + ") kennt vier "
    "Verfahren:")
d.table([
    ["Verfahren", "So funktioniert es", "Stärke", "Schwäche"],
    ["Fixed Window (gewählt)", "Ein Zähler pro festem Zeitfenster (z. B. 60 s). Am Ende des Fensters zurück auf 0.",
     "Einfach, gut erklärbar, Retry-After eindeutig", "Am Fensterrand bis zu doppelt so viele Anfragen möglich"],
    ["Sliding Window", "Das Fenster ist in Segmente geteilt (z. B. 6 × 10 s) und rutscht mit. Alte Segmente fallen weg.",
     "Gleichmäßiger, kein Randeffekt", "Etwas schwerer zu erklären"],
    ["Token Bucket", "Ein Eimer mit Marken. Jede Anfrage nimmt eine, in festen Abständen kommen neue dazu.",
     "Erlaubt kurze Spitzen, danach gleichmäßig", "Zwei Werte (Größe, Nachfüllrate) einzustellen"],
    ["Concurrency", "Begrenzt, wie viele Anfragen GLEICHZEITIG laufen, nicht pro Zeit.",
     "Schützt langsame Ressourcen", "Begrenzt keine Menge pro Minute"],
], [0.18, 0.40, 0.21, 0.21])
d.h2("Der Randeffekt beim Fixed Window")
d.code("""Grenze 10 pro 60 s
Fenster 1: 00:00–01:00           Fenster 2: 01:00–02:00
           ......... 10 × um 00:59 | 10 × um 01:00 .........
→ 20 Anfragen in 2 Sekunden, trotzdem alles erlaubt""")
d.p("Für Home Control ist das kein Problem: Auch 20 Login-Versuche in zwei Sekunden sind weit weg von den "
    "tausenden, die ein Angreifer bräuchte. Wichtiger war ein Verfahren, das man in der Präsentation in einem "
    "Satz erklären kann.")

# ------------------------------------------------------------------ 3
d.h1("3  429 und Retry-After")
d.table([
    ["Statuscode", "Bedeutung", "Hier verwendet"],
    ["401 Unauthorized", "Nicht angemeldet (oder Login falsch)", "falsches Passwort, kein Token"],
    ["403 Forbidden", "Angemeldet, aber nicht berechtigt", "User statt Admin"],
    ["429 Too Many Requests", "Zu viele Anfragen, später wieder versuchen", "Rate Limit, auch Kontosperre"],
    ["503 Service Unavailable", "Dienst nicht verfügbar", "Standard des Rate Limiters, hier auf 429 geändert"],
], [0.26, 0.40, 0.34])
d.p("Der Header <b>Retry-After</b> sagt dem Client, nach wie vielen Sekunden er es wieder versuchen darf. "
    "Der Fixed-Window-Limiter kennt das Ende des aktuellen Fensters und liefert den Wert als Metadaten mit:")
d.code("""HTTP/1.1 429 Too Many Requests
Retry-After: 42
Content-Type: application/problem+json

{ "status": 429,
  "title": "Zu viele Anmeldeversuche. Bitte in 42 Sekunden erneut versuchen.",
  "detail": "Diese Grenze schützt vor Passwort-Raten ..." }""")

# ------------------------------------------------------------------ 4
d.h1("4  Der Code")
d.h2("RateLimiting/RateLimitingRegistration.cs: die Regeln")
d.code("""services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // pro Gerät: Schlüssel = Client-IP
    options.AddPolicy("login", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unbekannt",
            _ => CreateFixedWindow(limit)));

    // für alle gemeinsam: immer derselbe Schlüssel
    options.AddPolicy("tuya", context =>
        RateLimitPartition.GetFixedWindowLimiter("alle", _ => CreateFixedWindow(limit)));

    options.OnRejected = WriteRejectionAsync;   // 429 + ProblemDetails + Retry-After
});""")
d.bullets([
    "<b>Partition:</b> Jeder Schlüssel bekommt seinen eigenen Zähler. Gleicher Schlüssel = gleicher Topf.",
    "<b>QueueLimit = 0:</b> Überzählige Anfragen warten nicht in einer Schlange, sie bekommen sofort 429.",
    "<b>Grenzen aus appsettings.json</b> (Abschnitt " + M + "RateLimiting" + E + "), gelesen erst bei der Anfrage. "
    "Werte 0 oder kleiner verhindern den Start (" + M + "ValidateOnStart" + E + ").",
])
d.h2("An den Endpunkten")
d.code("""auth.MapPost("/login", Login).AllowAnonymous().RequireRateLimiting("login");
auth.MapPost("/register", Register).AllowAnonymous().RequireRateLimiting("login");
auth.MapPost("/refresh", Refresh).AllowAnonymous().RequireRateLimiting("login");

var tuya = app.MapGroup("/api/tuya").RequireRateLimiting("tuya");   // ganze Gruppe""")
d.p("login, register und refresh teilen sich <b>einen</b> Zähler, weil sie dieselbe Regel und denselben "
    "Schlüssel benutzen. " + M + "GET /api/status" + E + " bekommt kein Limit: Der " + M + "TuyaOnlineCache" + E +
    " fragt Tuya ohnehin höchstens einmal pro Minute, ein Limit würde nur die LEDs mit 429 rot färben.")

# ------------------------------------------------------------------ 5
d.h1("5  Reihenfolge der Middleware")
d.code("""app.UseForwardedHeaders();   // 1. echte Client-IP setzen (siehe Abschnitt 6)
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseStaticFiles(...);
app.UseAuthentication();      // Wer bist du?
app.UseAuthorization();       // Darfst du das?   → sonst 401/403
app.UseRateLimiter();         // Wie oft schon?   → sonst 429
// ... Endpunkte""")
d.merksatz("Warum der Rate Limiter NACH der Autorisierung steht: POST /api/tuya/sync verlangt eine Anmeldung. "
           "Stünde der Limiter davor, könnte ein Fremder ohne Konto den gemeinsamen Tuya-Zähler leer laufen "
           "lassen, und alle angemeldeten Benutzer bekämen 429. So bekommt er schon vorher 401 und verbraucht "
           "nichts. Der Test Tuya_Anfragen_ohne_Anmeldung_verbrauchen_nichts prüft genau das.", warn=True)
d.p("UseForwardedHeaders steht ganz vorne, damit alles Spätere (Logs, Rate Limiting) schon die echte Adresse sieht.")

# ------------------------------------------------------------------ 6
d.h1("6  Das Proxy-Problem")
d.p("Von außen erreicht man Home Control nur über " + M + "tailscale serve" + E + ". Tailscale nimmt die "
    "HTTPS-Verbindung an und leitet sie an " + M + "http://127.0.0.1:5200" + E + " weiter. Für das Backend kommt "
    "deshalb <b>jede</b> Anfrage von 127.0.0.1, egal ob vom iPhone, iPad oder dem OVH-Server.")
d.code("""iPhone 100.a.b.c ──HTTPS──▶ tailscale serve ──HTTP──▶ Backend (sieht: 127.0.0.1)
                              setzt X-Forwarded-For: 100.a.b.c""")
d.p("Pro IP zu zählen, hieße dann: alle Geräte in einem Topf. Ein Gerät mit vielen Fehlversuchen würde alle anderen "
    "aussperren. Die Lösung ist der Header <b>X-Forwarded-For</b>, in den der Proxy die echte Adresse schreibt.")
d.h2("Vorher geprüft, nicht geraten")
d.table([
    ["Header", "Was tailscale serve macht (gemessen am 02.11.2026)"],
    ["X-Forwarded-For", "immer gesetzt auf die Tailscale-IP des Geräts; ein gefälschter Wert vom Client wird ersetzt"],
    ["Tailscale-User-Login", "Login des Benutzers; gefälschter Wert wird ersetzt; fehlt bei getaggten Geräten"],
    ["X-Forwarded-Proto/-Host", "https bzw. der Tailnet-Name (hier nicht gebraucht)"],
], [0.3, 0.7])
d.h2("Nie blind vertrauen")
d.p("Den Header kann jeder Client selbst mitschicken. Ihm zu glauben, ist nur sicher, wenn die Verbindung "
    "wirklich vom bekannten Proxy kommt:")
d.code("""builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor;  // nur die Adresse
    options.ForwardLimit = 1;                                   // genau ein Proxy
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
    options.KnownProxies.Add(IPAddress.Loopback);               // 127.0.0.1
    options.KnownProxies.Add(IPAddress.IPv6Loopback);           // ::1
});""")
d.p("Kommt eine Verbindung von einer anderen Adresse, ignoriert ASP.NET Core den Header. Gegenprobe beim Bau: "
    "Mit „allen Adressen vertrauen“ wurde der Test X_Forwarded_For_von_fremdem_Rechner_wird_ignoriert rot.")
d.h2("Und warum ist Tuya trotzdem global?")
d.p("Bei der Regel " + M + "tuya" + E + " wird nicht ein Gerät geschützt, sondern das <b>Tuya-Kontingent</b>, "
    "und das gibt es nur einmal. Pro Gerät hieße: Anzahl Geräte × Grenze. Ein gemeinsamer Zähler schützt das "
    "Kontingent unabhängig davon, woher die Last kommt.")

# ------------------------------------------------------------------ 7
d.h1("7  Was Rate Limiting nicht löst")
d.p("Die Kontosperre (5 Fehlversuche) lässt sich weiterhin auslösen, denn 5 Versuche liegen unter der Grenze "
    "von 10 pro Minute. Rate Limiting bremst Passwort-Raten über viele Konten stark, ein gezieltes Sperren eines "
    "einzelnen Kontos verhindert es aber nicht. Eine Grenze unter 5 wäre für echte Tippfehler zu streng.")

# ------------------------------------------------------------------ 8
d.h1("8  Logs")
d.code("""warn: Anmeldung fehlgeschlagen (falsche Zugangsdaten), Benutzer 3f2a…, von 100.a.b.c
warn: Anmeldung fehlgeschlagen (falsche Zugangsdaten), Benutzer unbekannt, von 100.a.b.c
warn: Rate Limit login überschritten: POST /api/auth/login von 100.a.b.c""")
d.bullets([
    "<b>Ins Log:</b> was passiert ist, welcher Benutzer (Id) und woher.",
    "<b>Nie ins Log:</b> Passwort, Token, die eingegebene E-Mail (dort landet manchmal versehentlich das Passwort).",
    "Die Benutzer-Id steht nur im Log, nicht in der HTTP-Antwort, sonst wüsste man, welche E-Mails es gibt.",
])

# ------------------------------------------------------------------ 9
d.h1("9  Tests")
d.table([
    ["Test", "prüft"],
    ["Login_nach_N_Versuchen_429_mit_Retry_After", "3 × 401, dann 429 mit Retry-After und deutschem Text"],
    ["Login_Register_und_Refresh_teilen_sich_einen_Zaehler", "ein Zähler für alle drei Endpunkte"],
    ["Anderes_Geraet_hat_ein_eigenes_Kontingent", "Gerät B (anderes X-Forwarded-For) ist nicht betroffen"],
    ["X_Forwarded_For_von_fremdem_Rechner_wird_ignoriert", "erfundene Header helfen einem Angreifer nicht"],
    ["Tuya_hat_einen_gemeinsamen_Zaehler_fuer_alle_Geraete", "Gerät A verbraucht, Gerät B bekommt 429"],
    ["Tuya_Anfragen_ohne_Anmeldung_verbrauchen_nichts", "Reihenfolge: Autorisierung vor Rate Limiter"],
    ["Status_hat_kein_Rate_Limit", "/api/status antwortet immer mit 200"],
    ["Ungueltige_Grenze_verhindert_den_Start", "PermitLimit 0 → App startet nicht"],
    ["LoginLoggingTests (3 Tests)", "Warnung im Log, ohne Passwort und ohne E-Mail"],
], [0.5, 0.5])
d.p("Die normale Testfabrik setzt großzügige Grenzen (100 000), sonst liefen die vielen Logins der übrigen Tests "
    "in 429. Weil die Grenzen erst bei der Anfrage gelesen werden, reicht dafür " + M + "ConfigureAppConfiguration" + E +
    ". Werte, die Program.cs schon beim Aufbau liest (z. B. Jwt:SigningKey), müssen dagegen per " + M + "UseSetting" +
    E + " gesetzt werden. Der TestServer hat keine echte Client-Adresse; " + M + "FakeRemoteIpStartupFilter" + E +
    " setzt sie wie hinter tailscale serve auf 127.0.0.1.")

# ------------------------------------------------------------------ 10
d.h1("10  Kurzantworten für Rückfragen")
d.table([
    ["Frage", "Antwort"],
    ["Was ist Rate Limiting?", "Eine Obergrenze für Anfragen pro Zeit. Darüber gibt es 429."],
    ["Warum Fixed Window?", "Am einfachsten zu erklären, Retry-After eindeutig. Der Randeffekt ist hier unkritisch."],
    ["Unterschied Token Bucket?", "Marken werden laufend nachgefüllt: erlaubt kurze Spitzen, danach gleichmäßig."],
    ["Warum 429 und nicht 503?", "429 heißt „du schickst zu viel“, 503 „der Server kann gerade nicht“."],
    ["Wozu Retry-After?", "Der Client weiß, wann er es wieder versuchen darf, statt blind zu wiederholen."],
    ["Warum pro IP beim Login?", "Damit ein Gerät mit Fehlversuchen die anderen nicht aussperrt."],
    ["Woher kennt das Backend die IP hinter dem Proxy?", "X-Forwarded-For von tailscale serve, nur akzeptiert von "
     "127.0.0.1/::1."],
    ["Warum Tuya global?", "Geschützt wird das gemeinsame Tuya-Kontingent, nicht ein einzelnes Gerät."],
    ["Warum steht der Limiter nach der Autorisierung?", "Fremde ohne Anmeldung sollen den gemeinsamen Zähler "
     "nicht leeren können."],
], [0.38, 0.62])

d.h1("11  Merksätze")
d.merksatz("• Rate Limiting = Obergrenze pro Zeit, darüber 429 Too Many Requests mit Retry-After.<br/>"
           "• Partition = Schlüssel für den Zähler: pro Gerät (IP) oder für alle gemeinsam.<br/>"
           "• Hinter einem Proxy sieht das Backend nur den Proxy. X-Forwarded-For nur vom bekannten Proxy glauben.<br/>"
           "• Reihenfolge: ForwardedHeaders zuerst, Rate Limiter nach der Autorisierung.<br/>"
           "• Ins Log: was, wer, woher. Nie Passwort oder Token.")

d.save("docs/Lernunterlage-Rate-Limiting.pdf")
