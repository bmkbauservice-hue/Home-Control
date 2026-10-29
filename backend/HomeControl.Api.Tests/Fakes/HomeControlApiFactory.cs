using HomeControl.Api.Contracts;
using HomeControl.Api.Data;
using HomeControl.Api.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace HomeControl.Api.Tests.Fakes;

// Startet die echte API im Arbeitsspeicher (ohne Port, ohne Netzwerk) für Endpunkt-Tests.
// Jede Factory bekommt ihre eigene SQLite-Datei im Temp-Ordner. Die echte homecontrol.db
// bleibt unberührt, die Datei wird am Ende gelöscht.
public class HomeControlApiFactory : WebApplicationFactory<Program>
{
    public const string AdminEmail = "admin@example.de";
    public const string UserEmail = "user@example.de";
    public const string Password = "Test-Passwort-123";

    private readonly string dbPath =
        Path.Combine(Path.GetTempPath(), $"homecontrol-test-{Guid.NewGuid():N}.db");

    private Task<(string AdminToken, string UserToken)>? tokens;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Development: Ohne Jwt:SigningKey nimmt die App dann einen zufälligen Schlüssel.
        builder.UseEnvironment("Development");

        // Läuft nach Program.cs und hängt eine weitere Einstellung an den DbContext an.
        // Das zweite UseSqlite ersetzt den Connection String aus appsettings.json.
        builder.ConfigureTestServices(services =>
            services.ConfigureDbContext<ApplicationDbContext>(options =>
                options.UseSqlite($"Data Source={dbPath}")));
    }

    // Legt einmal pro Factory einen Admin (erster Benutzer) und einen User an und meldet beide an.
    public Task<(string AdminToken, string UserToken)> GetTokensAsync() =>
        tokens ??= CreateTokensAsync();

    // Neuer Client, der das Access Token als "Authorization: Bearer ..." mitschickt.
    public HttpClient CreateClientWithToken(string accessToken)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    private async Task<(string AdminToken, string UserToken)> CreateTokensAsync()
    {
        using var anonymous = CreateClient();

        // Erster Benutzer: Registrierung ohne Anmeldung, wird automatisch Admin.
        var registerAdmin = await anonymous.PostAsJsonAsync(
            "/api/auth/register", new RegisterRequest(AdminEmail, Password, null));
        registerAdmin.EnsureSuccessStatusCode();
        var adminToken = await LoginAsync(anonymous, AdminEmail);

        // Weitere Benutzer darf nur ein Admin anlegen.
        using var admin = CreateClientWithToken(adminToken);
        var registerUser = await admin.PostAsJsonAsync(
            "/api/auth/register", new RegisterRequest(UserEmail, Password, Roles.User));
        registerUser.EnsureSuccessStatusCode();
        var userToken = await LoginAsync(anonymous, UserEmail);

        return (adminToken, userToken);
    }

    private static async Task<string> LoginAsync(HttpClient client, string email)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, Password));
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        return body!.AccessToken;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        // SQLite hält Dateien im Verbindungspool offen. Erst leeren, dann löschen.
        SqliteConnection.ClearAllPools();
        try
        {
            File.Delete(dbPath);
        }
        catch (IOException)
        {
            // Liegt nur im Temp-Ordner. Bleibt sie liegen, stört das keinen Test.
        }
    }
}
