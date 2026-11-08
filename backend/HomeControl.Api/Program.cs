using System.Net;
using System.Security.Cryptography;
using HomeControl.Api.Auth;
using HomeControl.Api.Blink;
using HomeControl.Api.Climate;
using HomeControl.Api.Data;
using HomeControl.Api.Demo;
using HomeControl.Api.Endpoints;
using HomeControl.Api.ErrorHandling;
using HomeControl.Api.Models;
using HomeControl.Api.LocalDevices;
using HomeControl.Api.RateLimiting;
using HomeControl.Api.Realtime;
using HomeControl.Api.Scale;
using HomeControl.Api.Status;
using HomeControl.Api.Tuya;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;

// Docker-Gesundheitsprüfung (HEALTHCHECK im Dockerfile): nur die laufende App abfragen, nichts starten.
if (args.Contains(HealthProbe.Argument))
{
    Environment.Exit(await HealthProbe.RunAsync());
}

var builder = WebApplication.CreateBuilder(args);

// Demo-Modus (./demo-starten.sh, docs/DEMO.md): Beispieldaten, Fake-Tuya und Fake-Blink, eigene Datenbank.
// Passt die Umgebung nicht dazu (z. B. DemoMode im Dienst/Production), startet die App gar nicht.
DemoMode.EnsureConsistent(builder.Environment, builder.Configuration);
var demoMode = DemoMode.IsEnabled(builder.Configuration);

// Dauerbetrieb auf diesem Rechner (dienst-einrichten.sh): Der Dienst läuft als "Production", damit
// die sicheren Einstellungen greifen (Secure-Cookie, keine API-Doku, kein Ersatz-JWT-Schlüssel).
// Die Geheimnisse bleiben trotzdem an EINER Stelle, in den User Secrets. ASP.NET Core liest sie von
// sich aus nur im Development, deshalb hier ausdrücklich, aber nur mit dem Schalter
// HomeControl:UseUserSecrets (im Dienst als Umgebungsvariable HomeControl__UseUserSecrets=true).
if (!builder.Environment.IsDevelopment() && builder.Configuration.GetValue<bool>("HomeControl:UseUserSecrets"))
{
    // reloadOnChange: Neue Werte (z. B. ein Blink-Token aus tools/BlinkSpike) gelten ohne Neustart des Dienstes.
    builder.Configuration.AddUserSecrets<Program>(optional: false, reloadOnChange: true);
    // Später hinzugefügte Quellen gewinnen. Umgebungsvariablen noch einmal anhängen, damit z. B. der
    // Connection String aus dem Dienst weiterhin vor den User Secrets gilt.
    builder.Configuration.AddEnvironmentVariables();
}

// Datenbank: EF Core mit SQLite. Der Connection String steht in appsettings.json.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(connectionString));

// Raumklima besitzt eine eigene Datenbank. So ändern wir keine bestehenden Haus-Migrationen.
var climateFile = builder.Configuration["Climate:DatabasePath"] ?? Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "HomeControl", demoMode ? "homecontrol-climate-demo.db" : "homecontrol-climate.db");
// Die Demo darf niemals die echte Klima-Datenbank lesen oder beschreiben.
if (demoMode && !Path.GetFileName(climateFile).Contains("demo", StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("Die Klima-Datenbank im Demo-Modus muss 'demo' im Dateinamen haben.");
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(climateFile))!);
builder.Services.AddDbContext<ClimateDbContext>(options => options.UseSqlite($"Data Source={climateFile}"));

// Legt bei Bedarf Demo-Daten an (siehe Data/DatabaseSeeder.cs).
builder.Services.AddScoped<DatabaseSeeder>();

// Tuya-Cloud (nur lesend, es wird nie geschaltet). Zugangsdaten kommen aus den User Secrets, siehe README.
builder.Services.Configure<TuyaOptions>(builder.Configuration.GetSection(TuyaOptions.SectionName));
// Uhrzeit über TimeProvider, damit Tests die Zeit steuern können.
builder.Services.AddSingleton(TimeProvider.System);
// Singleton: Das Token bleibt zwischen den Anfragen erhalten. Der TuyaClient selbst ist
// als typisierter HttpClient "transient" und wird pro Anfrage neu erzeugt.
builder.Services.AddSingleton<TuyaTokenStore>();
var tuyaHttpClient = builder.Services.AddHttpClient<TuyaClient>((services, client) =>
{
    var tuya = services.GetRequiredService<IOptions<TuyaOptions>>().Value;
    client.BaseAddress = new Uri(tuya.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(10);
});
// Scoped wie der DbContext: überträgt den Tuya-Zustand auf ein Gerät in der Datenbank.
builder.Services.AddScoped<TuyaSwitchRecorder>();
// Liest und überträgt den Zustand aller mit Tuya verknüpften Geräte.
builder.Services.AddScoped<TuyaDeviceSyncer>();
// Gleicht im Hintergrund regelmäßig ab (Tuya:AutoSyncSeconds, Standard 15 Minuten, 0 = aus).
// AddHostedService: startet mit der App und wird beim Beenden sauber gestoppt.
builder.Services.AddHostedService<TuyaSyncBackgroundService>();
// Merkt sich, welche Tuya-Geräte online sind (Status-LEDs, GET /api/status). Gefüllt vom Abgleich,
// ohne Hintergrund-Abgleich höchstens alle 5 Minuten neu geladen (siehe Status/TuyaOnlineCache.cs).
builder.Services.AddSingleton<TuyaOnlineCache>();
builder.Services.AddSingleton<HomeControl.Api.GoogleHome.GoogleHomeBridge>();
// Verteilt Geräteänderungen an offene Browser-Verbindungen (GET /api/live/devices, docs/ECHTZEIT.md).
builder.Services.AddSingleton<DeviceChangeHub>();

// Data Protection: Schlüssel liegen außerhalb des Repos (überleben git clean/checkout).
// SetApplicationName hält den Schlüssel unabhängig vom Pfad des Checkouts gleich – sonst
// wäre z. B. in einem Git-Worktree das gespeicherte Blink-Refresh-Token nicht entschlüsselbar.
// HomeControl:DataProtectionKeysPath: anderer Ordner, z. B. ein Docker-Volume (/keys), damit die Schlüssel einen
// neuen Container überleben. Sonst wäre das Blink-Token nach jedem Update nicht mehr entschlüsselbar.
var dataProtectionKeyPath = builder.Configuration["HomeControl:DataProtectionKeysPath"] ?? Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "HomeControl", demoMode ? "dataprotection-keys-demo" : "dataprotection-keys");
builder.Services.AddDataProtection()
    .SetApplicationName("HomeControl")
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeyPath));

// Blink-Kameras (nur lesend, keine offizielle API, siehe docs/BLINK.md). HardwareId kommt aus
// den User Secrets, das Refresh-Token aus der Datenbank (Bootstrap beim ersten Start ebenfalls
// aus den User Secrets, siehe BlinkCredentialStore).
builder.Services.Configure<BlinkOptions>(builder.Configuration.GetSection(BlinkOptions.SectionName));
builder.Services.AddScoped<BlinkCredentialStore>();
// Eigener schlanker Client statt Blink.NET (REVIEW.md M-5, docs/BLINK.md). Singleton: Access Token und
// letzter Stand bleiben erhalten. Für die Datenbank holt er sich über IServiceScopeFactory einen Scope.
// Der HttpClient kommt aus der IHttpClientFactory, ohne Standard-Header (kein alter App-Build).
builder.Services.AddHttpClient(BlinkHttpCameraClient.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(15));
// Echter Blink-Client oder (im Demo-Modus) Fake-Kameras, dazu im Demo die Fake-Tuya-Cloud hinter dem TuyaClient.
DemoMode.AddTuyaAndBlink(builder.Services, demoMode, tuyaHttpClient);

// Anmeldung mit JWT (siehe docs/AUTHENTIFIZIERUNG.md). Issuer, Audience und Laufzeiten stehen
// in appsettings.json, der geheime Signaturschlüssel in den User Secrets (Jwt:SigningKey).
var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
var usesTemporaryJwtKey = false;
if (jwtOptions.SigningKey.Length < JwtOptions.MinimumSigningKeyLength)
{
    // Außerhalb von Development: lieber gar nicht starten als mit einem unsicheren Schlüssel.
    // Ausnahme Demo-Modus: Dort gibt es nichts Echtes zu schützen, ein Wegwerf-Schlüssel pro Start reicht
    // (so startet die Demo auch im Container ohne .env).
    if (!builder.Environment.IsDevelopment() && !demoMode)
    {
        throw new InvalidOperationException(
            $"Jwt:SigningKey fehlt oder ist kürzer als {JwtOptions.MinimumSigningKeyLength} Zeichen.");
    }

    // Development ohne Schlüssel: Die App soll trotzdem starten (Tuya, Blink usw. laufen weiter).
    // Ein zufälliger Schlüssel nur für diesen Start. Nach einem Neustart sind alle Access Tokens
    // ungültig, das Frontend holt sich dann per Refresh einfach ein neues.
    jwtOptions.SigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
    usesTemporaryJwtKey = true;
}

// Singleton: Alle Teile (TokenService, JWT-Middleware) benutzen dieselben Einstellungen,
// auch denselben zufälligen Ersatzschlüssel.
builder.Services.AddSingleton(jwtOptions);
builder.Services.AddSingleton<TokenService>();
builder.Services.AddScoped<AuthService>();
// Benutzer verwalten und eigenes Passwort ändern (Einstellungsseite).
builder.Services.AddScoped<UserAdminService>();
// ASP.NET Core Identity: Benutzer, Passwort-Hashing (PBKDF2 mit Salt), Rollen, Sperre nach Fehlversuchen.
// Details und Regeln in Auth/IdentityRegistration.cs, Erklärung in docs/IDENTITY.md.
builder.Services.AddHomeControlIdentity();

// JWT-Bearer-Middleware: liest bei jeder Anfrage den Header "Authorization: Bearer <token>",
// prüft Signatur, Aussteller, Empfänger und Ablaufzeit und setzt daraus HttpContext.User.
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Claim-Namen so lassen, wie sie im Token stehen ("sub", "email", "role"), statt sie in
        // lange Microsoft-Bezeichnungen umzubenennen.
        options.MapInboundClaims = false;
        options.TokenValidationParameters = TokenService.CreateValidationParameters(jwtOptions);
    })
    // Zweites Verfahren für den KI-Agenten: Header "X-Api-Key" (siehe Auth/ApiKeyAuthenticationHandler.cs).
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
        ApiKeyDefaults.AuthenticationScheme, _ => { });
builder.Services.Configure<AgentOptions>(builder.Configuration.GetSection(AgentOptions.SectionName));

// Berechtigungsregeln: "AdminOnly" verlangt die Rolle Admin (siehe Auth/AuthPolicies.cs).
// Schreibende Endpunkte (Anlegen, Ändern, Löschen) benutzen diese Regel.
builder.Services.AddAuthorizationBuilder().AddHomeControlPolicies();

// Schalter "Auth:RequireLoginForReading" (Standard: aus). Ist er an, verlangt jeder Endpunkt ohne
// eigene Regel eine Anmeldung (JWT oder API-Schlüssel), also auch alle GET-Aufrufe. Erst einschalten,
// wenn der Agent seinen Schlüssel hat, sonst bekommt er 401. Der Schalter wird erst beim ersten
// Gebrauch gelesen, damit auch Einstellungen aus den Tests ankommen.
builder.Services.AddOptions<AuthorizationOptions>().Configure<IConfiguration>((options, configuration) =>
{
    if (configuration.GetValue<bool>("Auth:RequireLoginForReading"))
    {
        options.FallbackPolicy = AuthPolicies.CreateReadingPolicy();
    }
});

// Gesundheitsprüfung für Docker: GET /healthz (App läuft, Datenbank erreichbar), siehe Status/Health.cs.
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("datenbank");

// OpenAPI-Beschreibung der API (für die Scalar-Oberfläche)
builder.Services.AddOpenApi();

// Wetterdienst (Open-Meteo): fünf Minuten Zwischenspeicher, kurzer Timeout.
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<ScaleSettingsStore>();
ShellyMeters.Register(builder.Services);
builder.Services.AddSingleton<ScaleReadGate>();
builder.Services.AddHttpClient("weather", client => client.Timeout = TimeSpan.FromSeconds(8));

// Einheitliche Fehlerantworten im ProblemDetails-Format (RFC 9457)
builder.Services.AddProblemDetails();

// Eingebaute Validierung von .NET 10: prüft DataAnnotations an den Parametern der Endpunkte
// (z. B. [Required], [EmailAddress] an LoginRequest), bevor der Endpunkt läuft. Ungültig → 400
// mit ProblemDetails, der Endpunkt wird nicht aufgerufen.
builder.Services.AddValidation();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// Fehlerhafte Anfragen (z. B. kaputtes JSON) immer als Exception melden – sonst passiert das
// nur im Development-Modus. So beantwortet der GlobalExceptionHandler sie überall gleich (400).
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);

// Hinter "tailscale serve" kommt jede Verbindung von 127.0.0.1. Die echte Adresse des Geräts schickt
// Tailscale im Header X-Forwarded-For mit (es überschreibt dabei einen vom Client gefälschten Wert).
// Diesem Header glauben wir NUR, wenn die Verbindung selbst von 127.0.0.1 bzw. ::1 kommt. Sonst könnte
// jeder mit einem erfundenen Header so tun, als wäre er ein anderes Gerät. Gebraucht wird das für das
// Rate Limiting pro Gerät (siehe RateLimiting/RateLimitingRegistration.cs, docs/RATE-LIMITING.md).
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    // Nur die Client-Adresse. Host und Protokoll übernehmen wir bewusst nicht.
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor;
    // Genau ein Proxy (tailscale serve): nur den letzten Eintrag im Header auswerten.
    options.ForwardLimit = 1;
    // Standardlisten leeren und nur die beiden Loopback-Adressen eintragen, damit klar ist, wem wir trauen.
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
    options.KnownProxies.Add(IPAddress.Loopback);
    options.KnownProxies.Add(IPAddress.IPv6Loopback);
});

// Rate Limiting (REVIEW.md M-2, M-3): Login pro Gerät, Tuya für alle gemeinsam. Grenzen in appsettings.json.
builder.Services.AddHomeControlRateLimiting(builder.Configuration);

var app = builder.Build();

// Ganz vorne: Ab hier ist Connection.RemoteIpAddress die echte Adresse des Geräts (siehe oben).
// Alles Spätere (Logs, Rate Limiting) sieht schon die richtige Adresse.
app.UseForwardedHeaders();

// Reihenfolge ist wichtig: Der Exception Handler steht weit vorne,
// damit er Fehler aus allen folgenden Schritten abfangen kann.
app.UseExceptionHandler();

// Antworten mit Fehlercode, aber ohne Inhalt (z. B. 404 für eine unbekannte Route
// oder eine ungültige GUID in der URL), bekommen ebenfalls ProblemDetails.
app.UseStatusCodePages();

// Gebautes Frontend ausliefern (ein Port, ein Link, kein CORS). Nur aktiv, wenn dort eine index.html liegt:
// Frontend:Path (z. B. ../../frontend/dist) oder standardmäßig der Ordner wwwroot neben dem Backend.
// Ohne gebautes Frontend ändert sich nichts (Entwicklung läuft weiter über den Vite-Server).
var frontendPath = Path.GetFullPath(
    app.Configuration["Frontend:Path"] ?? "wwwroot", app.Environment.ContentRootPath);
var servesFrontend = File.Exists(Path.Combine(frontendPath, "index.html"));
PhysicalFileProvider? frontendFiles = null;
if (servesFrontend)
{
    frontendFiles = new PhysicalFileProvider(frontendPath);
    // Statische Dateien stehen vor der Authentifizierung: Die Anmeldeseite muss auch ohne Login laden.
    // Geschützt sind die Daten unter /api, nicht die Oberfläche selbst.
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = frontendFiles });
    app.UseStaticFiles(new StaticFileOptions { FileProvider = frontendFiles });
}

// Erst authentifizieren (Wer bist du? Token lesen und prüfen), dann autorisieren (Was darfst
// du? z. B. RequireAuthorization, Rolle Admin). Beide nach dem Exception Handler, damit auch
// ihre Fehler einheitlich als ProblemDetails ankommen (401 und 403 über UseStatusCodePages).
app.UseAuthentication();
app.UseAuthorization();

// Rate Limiting NACH der Autorisierung: Eine Anfrage ohne Anmeldung bekommt schon vorher 401 und
// verbraucht so keine der gemeinsamen Tuya-Anfragen. Sonst könnte ein Fremder sie leer laufen lassen.
app.UseRateLimiter();

if (usesTemporaryJwtKey)
{
    app.Logger.LogWarning(
        "Kein Jwt:SigningKey in den User Secrets. Es wird ein zufälliger Schlüssel nur für diesen Start benutzt. " +
        "Einrichten mit ./jwt-schluessel-einrichten.sh (siehe docs/AUTHENTIFIZIERUNG.md).");
}

// Beim Start fehlende Migrationen automatisch anwenden.
// So wird die SQLite-Datei angelegt und passt immer zum aktuellen Modell.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.Migrate();

    // Demo-Daten nur in Development und nur, wenn "SeedDemoData" ausdrücklich true ist.
    if (app.Environment.IsDevelopment() && app.Configuration.GetValue<bool>("SeedDemoData"))
    {
        var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();
        await seeder.SeedAsync();
    }

    // Demo-Modus: Beispielhaus (nur in die leere Demo-Datenbank) und Demo-Konto mit dem Passwort aus dem Skript.
    if (demoMode)
    {
        var demoSeeder = scope.ServiceProvider.GetRequiredService<DemoDataSeeder>();
        await demoSeeder.SeedAsync(app.Configuration[DemoMode.PasswordSetting]!);
        app.Logger.LogWarning("DEMO-MODUS: Beispieldaten, Fake-Tuya und Fake-Blink, keine echten Aufrufe. Konto {Email}.",
            DemoHouse.Email);
    }
}

if (app.Environment.IsDevelopment())
{
    // API-Dokumentation unter /scalar
    // Die Dokumentation bleibt auch mit "RequireLoginForReading" erreichbar (nur im Development).
    app.MapOpenApi().AllowAnonymous();
    app.MapScalarApiReference().AllowAnonymous();
}

if (!servesFrontend)
{
    app.MapGet("/", () => "Hello World!").AllowAnonymous();
}

app.MapRoomEndpoints();
app.MapDeviceEndpoints();
app.MapDeviceEventEndpoints();
app.MapReportEndpoints();
app.MapTuyaEndpoints();
app.MapWeatherEndpoints();
app.MapClimateEndpoints();
app.MapScaleEndpoints();
ShellyMeters.Map(app);
app.MapTuyaControlEndpoints();
HomeControl.Api.GoogleHome.GoogleHomeEndpoints.MapGoogleHomeEndpoints(app);
app.MapBlinkEndpoints();
app.MapAuthEndpoints();
app.MapStatusEndpoints();
app.MapRealtimeEndpoints();
app.MapDemoEndpoints();
app.MapUserEndpoints();
app.MapSystemEndpoints();
// Ohne Anmeldung: Docker fragt hier, ob der Container gesund ist. Die Antwort verrät nur "Healthy" oder "Unhealthy".
app.MapHealthChecks("/healthz").AllowAnonymous();

if (frontendFiles is not null)
{
    var files = frontendFiles;
    // Alles, was keine Datei und keine API-Route ist, bekommt die index.html (React-Oberfläche).
    // Unbekannte Adressen unter /api bleiben ein echter 404 und werden nicht mit HTML beantwortet.
    app.MapFallback(async context =>
    {
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.SendFileAsync(files.GetFileInfo("index.html"));
    }).AllowAnonymous();
}

app.Run();

// Für die Endpunkt-Tests (WebApplicationFactory<Program> in HomeControl.Api.Tests).
public partial class Program { }

