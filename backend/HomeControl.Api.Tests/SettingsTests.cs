using System.Net;
using System.Net.Http.Json;
using HomeControl.Api.Contracts;
using HomeControl.Api.Tests.Fakes;
using Microsoft.AspNetCore.Mvc;

namespace HomeControl.Api.Tests;

// Einstellungsseite: Benutzer verwalten (nur Admin), eigenes Passwort ändern, Info.
// Jeder Test, der Benutzer ändert, startet seine eigene API (eigene Datenbank), damit sich die Tests nicht stören.
public class SettingsTests
{
    private const string NewPassword = "Neues-Passwort-456";

    private static async Task<(HomeControlApiFactory Factory, HttpClient Admin, HttpClient User)> StartAsync()
    {
        var factory = new HomeControlApiFactory();
        var (adminToken, userToken) = await factory.GetTokensAsync();
        return (factory, factory.CreateClientWithToken(adminToken), factory.CreateClientWithToken(userToken));
    }

    private static async Task<List<UserListResponse>> UsersAsync(HttpClient admin) =>
        (await admin.GetFromJsonAsync<List<UserListResponse>>("/api/users"))!;

    private static async Task<string?> TitleAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ProblemDetails>())?.Title;

    // ---------- Benutzer verwalten ----------

    [Fact]
    public async Task Benutzerliste_nur_fuer_Admin()
    {
        var (factory, admin, user) = await StartAsync();
        using (factory) using (admin) using (user)
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/users")).StatusCode);

            var users = await UsersAsync(admin);
            Assert.Equal([HomeControlApiFactory.AdminEmail, HomeControlApiFactory.UserEmail], users.Select(u => u.Email));
            Assert.Equal(["Admin", "User"], users.Select(u => u.Role));
        }
    }

    [Fact]
    public async Task Rolle_aendern_und_zuruecksetzen()
    {
        var (factory, admin, user) = await StartAsync();
        using (factory) using (admin) using (user)
        {
            var userId = (await UsersAsync(admin)).Single(u => u.Role == "User").Id;

            var promote = await admin.PutAsJsonAsync($"/api/users/{userId}/role", new ChangeRoleRequest("Admin"));
            Assert.Equal(HttpStatusCode.NoContent, promote.StatusCode);
            Assert.All(await UsersAsync(admin), u => Assert.Equal("Admin", u.Role));

            var demote = await admin.PutAsJsonAsync($"/api/users/{userId}/role", new ChangeRoleRequest("User"));
            Assert.Equal(HttpStatusCode.NoContent, demote.StatusCode);
            Assert.Equal("User", (await UsersAsync(admin)).Single(u => u.Id == userId).Role);

            var invalid = await admin.PutAsJsonAsync($"/api/users/{userId}/role", new ChangeRoleRequest("Chef"));
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
    }

    [Fact]
    public async Task Sich_selbst_loeschen_oder_herabstufen_geht_nicht()
    {
        var (factory, admin, user) = await StartAsync();
        using (factory) using (admin) using (user)
        {
            var adminId = (await UsersAsync(admin)).Single(u => u.Role == "Admin").Id;

            var demote = await admin.PutAsJsonAsync($"/api/users/{adminId}/role", new ChangeRoleRequest("User"));
            var delete = await admin.DeleteAsync($"/api/users/{adminId}");

            Assert.Equal(HttpStatusCode.Conflict, demote.StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, delete.StatusCode);
            Assert.Contains("eigene Konto", await TitleAsync(delete));
        }
    }

    [Fact]
    public async Task Der_letzte_Admin_bleibt_auch_mit_altem_Token_eines_anderen()
    {
        var (factory, admin, user) = await StartAsync();
        using (factory) using (admin) using (user)
        {
            var users = await UsersAsync(admin);
            var adminId = users.Single(u => u.Role == "Admin").Id;
            var userId = users.Single(u => u.Role == "User").Id;

            // Der User wird Admin und meldet sich neu an (Token mit Rolle Admin) ...
            await admin.PutAsJsonAsync($"/api/users/{userId}/role", new ChangeRoleRequest("Admin"));
            var login = await factory.CreateClient().PostAsJsonAsync("/api/auth/login",
                new LoginRequest(HomeControlApiFactory.UserEmail, HomeControlApiFactory.Password));
            using var second = factory.CreateClientWithToken((await login.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken);
            // ... wird gleich wieder herabgestuft, hat aber noch bis zu 15 Minuten ein Token mit "Admin".
            await admin.PutAsJsonAsync($"/api/users/{userId}/role", new ChangeRoleRequest("User"));

            var delete = await second.DeleteAsync($"/api/users/{adminId}");

            Assert.Equal(HttpStatusCode.Conflict, delete.StatusCode);
            Assert.Contains("letzte Admin", await TitleAsync(delete));
            Assert.Equal(2, (await UsersAsync(admin)).Count);
        }
    }

    [Fact]
    public async Task Geloeschter_Benutzer_kann_sich_nicht_mehr_anmelden()
    {
        var (factory, admin, user) = await StartAsync();
        using (factory) using (admin) using (user)
        {
            var userId = (await UsersAsync(admin)).Single(u => u.Role == "User").Id;

            Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/users/{userId}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync($"/api/users/{userId}")).StatusCode);

            using var anonymous = factory.CreateClient();
            var login = await anonymous.PostAsJsonAsync("/api/auth/login",
                new LoginRequest(HomeControlApiFactory.UserEmail, HomeControlApiFactory.Password));
            Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
        }
    }

    // ---------- Eigenes Passwort ----------

    [Fact]
    public async Task Passwort_aendern_prueft_das_alte_und_die_Regeln()
    {
        var (factory, admin, user) = await StartAsync();
        using (factory) using (admin) using (user)
        {
            var wrong = await user.PostAsJsonAsync("/api/auth/password", new ChangePasswordRequest("falsch", NewPassword));
            var weak = await user.PostAsJsonAsync("/api/auth/password", new ChangePasswordRequest(HomeControlApiFactory.Password, "kurz"));

            Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
            Assert.Contains("currentPassword", (await wrong.Content.ReadFromJsonAsync<ValidationProblemDetails>())!.Errors.Keys);
            Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
            Assert.Contains("newPassword", (await weak.Content.ReadFromJsonAsync<ValidationProblemDetails>())!.Errors.Keys);
        }
    }

    [Fact]
    public async Task Neues_Passwort_gilt_und_andere_Sitzungen_werden_beendet()
    {
        using var factory = new HomeControlApiFactory();
        await factory.GetTokensAsync();
        // Zwei Sitzungen desselben Benutzers, z. B. Handy und Laptop. Jeder Client hat sein eigenes Refresh-Cookie.
        using var phone = factory.CreateClient();
        using var laptop = factory.CreateClient();
        var credentials = new LoginRequest(HomeControlApiFactory.UserEmail, HomeControlApiFactory.Password);
        (await phone.PostAsJsonAsync("/api/auth/login", credentials)).EnsureSuccessStatusCode();
        var laptopLogin = await laptop.PostAsJsonAsync("/api/auth/login", credentials);
        var token = (await laptopLogin.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken;
        laptop.DefaultRequestHeaders.Authorization = new("Bearer", token);

        var change = await laptop.PostAsJsonAsync("/api/auth/password",
            new ChangePasswordRequest(HomeControlApiFactory.Password, NewPassword));

        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);
        // Das Handy ist abgemeldet. Wichtig: Sein Versuch darf NICHT die Reuse Detection auslösen,
        // sonst wäre danach auch der Laptop abgemeldet (deshalb werden die Tokens gelöscht, nicht widerrufen).
        Assert.Equal(HttpStatusCode.Unauthorized, (await phone.PostAsync("/api/auth/refresh", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await laptop.PostAsync("/api/auth/refresh", null)).StatusCode);

        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/auth/login", credentials)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(HomeControlApiFactory.UserEmail, NewPassword))).StatusCode);
    }

    // ---------- Info ----------

    [Fact]
    public async Task Info_nennt_Umgebung_und_Datenbank_ohne_Pfad_nur_fuer_Admin()
    {
        var (factory, admin, user) = await StartAsync();
        using (factory) using (admin) using (user)
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/system/info")).StatusCode);

            var info = await admin.GetFromJsonAsync<SystemInfoResponse>("/api/system/info");

            Assert.Equal("Development", info!.Environment);
            Assert.False(info.DemoMode);
            Assert.StartsWith("homecontrol-test-", info.DatabaseFile);
            Assert.DoesNotContain("/", info.DatabaseFile);
            Assert.True(info.DatabaseBytes > 0);
            Assert.Equal(2, info.Users);
            Assert.StartsWith(".NET", info.DotNetVersion);
        }
    }
}
