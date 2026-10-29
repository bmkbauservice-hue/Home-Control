using System.Security.Cryptography;
using HomeControl.Api.Auth;
using HomeControl.Api.Blink;
using HomeControl.Api.Data;
using HomeControl.Api.Endpoints;
using HomeControl.Api.ErrorHandling;
using HomeControl.Api.Models;
using HomeControl.Api.Tuya;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Datenbank: EF Core mit SQLite. Der Connection String steht in appsettings.json.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(connectionString));

// Legt bei Bedarf Demo-Daten an (siehe Data/DatabaseSeeder.cs).
builder.Services.AddScoped<DatabaseSeeder>();

// Tuya-Cloud (nur lesend, es wird nie geschaltet). Zugangsdaten kommen aus den User Secrets, siehe README.
builder.Services.Configure<TuyaOptions>(builder.Configuration.GetSection(TuyaOptions.SectionName));
// Uhrzeit über TimeProvider, damit Tests die Zeit steuern können.
builder.Services.AddSingleton(TimeProvider.System);
// Singleton: Das Token bleibt zwischen den Anfragen erhalten. Der TuyaClient selbst ist
// als typisierter HttpClient "transient" und wird pro Anfrage neu erzeugt.
builder.Services.AddSingleton<TuyaTokenStore>();
builder.Services.AddHttpClient<TuyaClient>((services, client) =>
{
    var tuya = services.GetRequiredService<IOptions<TuyaOptions>>().Value;
    client.BaseAddress = new Uri(tuya.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(10);
});
// Scoped wie der DbContext: überträgt den Tuya-Zustand auf ein Gerät in der Datenbank.
builder.Services.AddScoped<TuyaSwitchRecorder>();
// Liest und überträgt den Zustand aller mit Tuya verknüpften Geräte.
builder.Services.AddScoped<TuyaDeviceSyncer>();

// Data Protection: Schlüssel liegen außerhalb des Repos (überleben git clean/checkout).
// SetApplicationName hält den Schlüssel unabhängig vom Pfad des Checkouts gleich – sonst
// wäre z. B. in einem Git-Worktree das gespeicherte Blink-Refresh-Token nicht entschlüsselbar.
var dataProtectionKeyPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "HomeControl", "dataprotection-keys");
builder.Services.AddDataProtection()
    .SetApplicationName("HomeControl")
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeyPath));

// Blink-Kameras (nur lesend, keine offizielle API, siehe docs/BLINK.md). HardwareId kommt aus
// den User Secrets, das Refresh-Token aus der Datenbank (Bootstrap beim ersten Start ebenfalls
// aus den User Secrets, siehe BlinkCredentialStore).
builder.Services.Configure<BlinkOptions>(builder.Configuration.GetSection(BlinkOptions.SectionName));
builder.Services.AddScoped<BlinkCredentialStore>();
// Singleton: Der zugrunde liegende BlinkClient (Blink.NET) hält selbst den Login-Zustand.
// Für die Datenbank holt er sich bei Bedarf über IServiceScopeFactory einen eigenen Scope.
builder.Services.AddSingleton<IBlinkCameraClient, BlinkNetCameraClient>();

// Anmeldung mit JWT (siehe docs/AUTHENTIFIZIERUNG.md). Issuer, Audience und Laufzeiten stehen
// in appsettings.json, der geheime Signaturschlüssel in den User Secrets (Jwt:SigningKey).
var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
var usesTemporaryJwtKey = false;
if (jwtOptions.SigningKey.Length < JwtOptions.MinimumSigningKeyLength)
{
    // Außerhalb von Development: lieber gar nicht starten als mit einem unsicheren Schlüssel.
    if (!builder.Environment.IsDevelopment())
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
// Passwort-Hashing aus ASP.NET Core Identity (PBKDF2 mit Salt), ohne den Rest von Identity.
builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();

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

// OpenAPI-Beschreibung der API (für die Scalar-Oberfläche)
builder.Services.AddOpenApi();

// Einheitliche Fehlerantworten im ProblemDetails-Format (RFC 9457)
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// Fehlerhafte Anfragen (z. B. kaputtes JSON) immer als Exception melden – sonst passiert das
// nur im Development-Modus. So beantwortet der GlobalExceptionHandler sie überall gleich (400).
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);

var app = builder.Build();

// Reihenfolge ist wichtig: Der Exception Handler steht ganz vorne,
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
app.MapBlinkEndpoints();
app.MapAuthEndpoints();

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
