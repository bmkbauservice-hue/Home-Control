using HomeControl.Api.Data;
using HomeControl.Api.Endpoints;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Datenbank: EF Core mit SQLite. Der Connection String steht in appsettings.json.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(connectionString));

// OpenAPI-Beschreibung der API (für die Scalar-Oberfläche)
builder.Services.AddOpenApi();

var app = builder.Build();

// Beim Start fehlende Migrationen automatisch anwenden.
// So wird die SQLite-Datei angelegt und passt immer zum aktuellen Modell.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.Migrate();
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

app.Run();
