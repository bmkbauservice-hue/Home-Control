using HomeControl.Api.Data;
using HomeControl.Api.Endpoints;
using HomeControl.Api.ErrorHandling;
using HomeControl.Api.Tuya;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Datenbank: EF Core mit SQLite. Der Connection String steht in appsettings.json.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(connectionString));

// Legt bei Bedarf Demo-Daten an (siehe Data/DatabaseSeeder.cs).
builder.Services.AddScoped<DatabaseSeeder>();

// Tuya-Cloud (nur lesend). Zugangsdaten kommen aus den User Secrets, siehe README.
builder.Services.Configure<TuyaOptions>(builder.Configuration.GetSection(TuyaOptions.SectionName));
builder.Services.AddHttpClient<TuyaClient>((services, client) =>
{
    var tuya = services.GetRequiredService<IOptions<TuyaOptions>>().Value;
    client.BaseAddress = new Uri(tuya.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(10);
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
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapGet("/", () => "Hello World!");

app.MapRoomEndpoints();
app.MapDeviceEndpoints();
app.MapDeviceEventEndpoints();
app.MapReportEndpoints();
app.MapTuyaEndpoints();

app.Run();
