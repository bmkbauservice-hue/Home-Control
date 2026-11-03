using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HomeControl.Api.Contracts;
using HomeControl.Api.Data;
using HomeControl.Api.Demo;
using HomeControl.Api.Tests.Fakes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting.Internal;

namespace HomeControl.Api.Tests;

// Demo-Modus (docs/DEMO.md): startet mit Beispielhaus, Fake-Tuya und Fake-Blink. Vor allem aber:
// Er darf nie versehentlich im Dienst (Production) oder beim Entwickeln an sein.
public class DemoModeTests(DemoModeTests.DemoApiFactory factory) : IClassFixture<DemoModeTests.DemoApiFactory>
{
    public const string DemoPassword = "Demo-Test-2026";

    // Umgebung "Demo" wie bei ./demo-starten.sh: appsettings.Demo.json wird geladen (die echte Datei aus dem Repo).
    // Die Datenbank ersetzt die Basisklasse durch eine Temp-Datei, die homecontrol-demo.db entsteht nicht.
    // Tuya:AutoSyncSeconds bleibt 0 (Basisklasse), damit der Abgleich nur läuft, wenn der Test ihn auslöst.
    public class DemoApiFactory : HomeControlApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseEnvironment(DemoMode.EnvironmentName);
            builder.UseSetting("Jwt:SigningKey", ProductionModeTests.ProductionApiFactory.SigningKey);
            builder.UseSetting(DemoMode.PasswordSetting, DemoPassword);
        }

        public async Task<HttpClient> CreateDemoClientAsync()
        {
            var client = CreateClient();
            var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(DemoHouse.Email, DemoPassword));
            login.EnsureSuccessStatusCode();
            var body = await login.Content.ReadFromJsonAsync<LoginResponse>();
            Assert.Equal("Admin", body!.User.Role);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.AccessToken);
            return client;
        }
    }

    // ---------- Start und Beispielhaus ----------

    [Fact]
    public async Task Demo_startet_mit_5_Raeumen_12_Geraeten_und_7_Tagen_Ereignissen()
    {
        using var client = await factory.CreateDemoClientAsync();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(5, await db.Rooms.CountAsync());
        Assert.Equal(12, await db.Devices.CountAsync());
        Assert.All(await db.Devices.ToListAsync(), d => Assert.StartsWith("demo", d.TuyaDeviceId));

        var timestamps = (await db.DeviceEvents.ToListAsync()).Select(e => e.Timestamp).ToList();
        Assert.True(timestamps.Count > 100);
        Assert.True(timestamps.Max() - timestamps.Min() > TimeSpan.FromDays(5));
        Assert.True(timestamps.Max() <= DateTimeOffset.UtcNow);
    }

    // Fehler aus der ersten Fassung: Ids mit Bindestrich ("demo-computer") lehnt DeviceEndpoints ab, weil echte
    // Tuya-Ids nur Buchstaben und Ziffern haben. Dann ließ sich in der Demo kein Gerät bearbeiten.
    [Fact]
    public async Task Jedes_Demo_Geraet_laesst_sich_bearbeiten()
    {
        using var client = await factory.CreateDemoClientAsync();
        var devices = await client.GetFromJsonAsync<List<DeviceResponse>>("/api/devices");

        foreach (var d in devices!)
        {
            var update = await client.PutAsJsonAsync($"/api/devices/{d.Id}",
                new DeviceRequest(d.Name, d.Type, d.RoomId, d.TuyaDeviceId, d.TuyaSyncSeconds));
            Assert.True(update.IsSuccessStatusCode, $"{d.Name}: {await update.Content.ReadAsStringAsync()}");
        }
    }

    [Fact]
    public async Task Banner_Endpunkt_meldet_Demo_ohne_Anmeldung()
    {
        using var anonymous = factory.CreateClient();

        var info = await anonymous.GetFromJsonAsync<DemoInfoResponse>("/api/demo");

        Assert.True(info!.DemoMode);
        Assert.Equal(DemoHouse.Email, info.DemoEmail);
    }

    [Fact]
    public void Demo_Einstellungen_Leseschutz_an_und_Tuya_Adresse_ins_Leere()
    {
        // Die Testfabrik schaltet den Leseschutz für alle Tests aus, deshalb hier direkt an der Datei prüfen.
        var demo = new ConfigurationBuilder().AddJsonFile(RepoFile("backend/HomeControl.Api/appsettings.Demo.json")).Build();

        Assert.True(DemoMode.IsEnabled(demo));
        Assert.True(demo.GetValue<bool>("Auth:RequireLoginForReading"));
        // .invalid ist eine Endung, die es nie geben wird: Selbst ohne Fake ginge keine Anfrage an Tuya.
        Assert.EndsWith(".invalid", demo["Tuya:BaseUrl"]);
        Assert.Contains("homecontrol-demo.db", demo["ConnectionStrings:DefaultConnection"]);
    }

    [Fact]
    public async Task Abgleich_liest_die_Fake_Cloud_Watt_Akku_und_ein_Geraet_offline()
    {
        using var client = await factory.CreateDemoClientAsync();

        var sync = await client.PostAsync("/api/tuya/sync", null);
        Assert.Equal(HttpStatusCode.OK, sync.StatusCode);

        var devices = await client.GetFromJsonAsync<List<DeviceResponse>>("/api/devices");
        Assert.Equal(15, devices!.Single(d => d.Name == "Haustür").BatteryPercent);
        Assert.NotNull(devices.Single(d => d.Name == "Kühlschrank").PowerWatt);
        // Ein ausgestecktes Gerät bekommt keine (alten) Messwerte.
        Assert.Null(devices.Single(d => d.Name == "Drucker").PowerWatt);

        var status = await client.GetFromJsonAsync<StatusResponse>("/api/status");
        Assert.Equal("online", status!.Tuya);
        Assert.Equal(1, status.DevicesOffline);
        Assert.Equal(11, status.DevicesOnline);
    }

    [Fact]
    public async Task Zwei_Demo_Kameras_mit_Bild()
    {
        using var client = await factory.CreateDemoClientAsync();

        var cameras = await client.GetFromJsonAsync<List<BlinkCameraResponse>>("/api/blink/cameras");
        Assert.Equal(["Haustür", "Garten"], cameras!.Select(c => c.Name));

        var image = await client.GetAsync($"/api/blink/cameras/{cameras![0].Id}/thumbnail");
        Assert.Equal("image/jpeg", image.Content.Headers.ContentType?.MediaType);
        Assert.True((await image.Content.ReadAsByteArrayAsync()).Length > 1000);
    }

    // ---------- Nie versehentlich an ----------

    // Startet die App mit der Umgebung und zusätzlichen Einstellungen. Sie soll gar nicht erst hochfahren.
    private sealed class MisconfiguredFactory(string environment, Dictionary<string, string> settings) : HomeControlApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseEnvironment(environment);
            builder.UseSetting("Jwt:SigningKey", ProductionModeTests.ProductionApiFactory.SigningKey);
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }
        }
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    public void DemoMode_im_Dienst_oder_beim_Entwickeln_verhindert_den_Start(string environment)
    {
        using var app = new MisconfiguredFactory(environment, new()
        {
            [DemoMode.Setting] = "true",
            [DemoMode.PasswordSetting] = DemoPassword,
        });

        var exception = Assert.ThrowsAny<Exception>(() => app.CreateClient());

        Assert.Contains("nur in der Umgebung \"Demo\"", exception.ToString());
    }

    [Theory]
    [InlineData("Data Source=/pfad/homecontrol.db", "eigene Datei")]
    [InlineData(null, "UseUserSecrets")]
    public void Demo_mit_echter_Datenbank_oder_User_Secrets_verhindert_den_Start(string? connection, string expected)
    {
        var settings = new Dictionary<string, string?>
        {
            [DemoMode.Setting] = "true",
            [DemoMode.PasswordSetting] = DemoPassword,
            ["ConnectionStrings:DefaultConnection"] = connection ?? "Data Source=homecontrol-demo.db",
            ["HomeControl:UseUserSecrets"] = connection is null ? "true" : "false",
        };

        var exception = Assert.Throws<InvalidOperationException>(() => DemoMode.EnsureConsistent(Environment("Demo"), Config(settings)));

        Assert.Contains(expected, exception.Message);
    }

    [Fact]
    public void Umgebung_Demo_ohne_DemoMode_oder_ohne_Passwort_verhindert_den_Start()
    {
        Assert.Throws<InvalidOperationException>(() => DemoMode.EnsureConsistent(Environment("Demo"), Config([])));
        Assert.Throws<InvalidOperationException>(() => DemoMode.EnsureConsistent(Environment("Demo"), Config(new()
        {
            [DemoMode.Setting] = "true",
            ["ConnectionStrings:DefaultConnection"] = "Data Source=homecontrol-demo.db",
        })));
    }

    [Fact]
    public void Ohne_DemoMode_aendert_sich_im_Dienst_nichts()
    {
        DemoMode.EnsureConsistent(Environment("Production"), Config([]));
        DemoMode.EnsureConsistent(Environment("Production"), Config(new() { [DemoMode.Setting] = "false" }));
    }

    [Fact]
    public void Dienst_und_Entwicklung_schalten_DemoMode_nirgends_ein()
    {
        var api = RepoFile("backend/HomeControl.Api");
        foreach (var file in new[] { "appsettings.json", "appsettings.Development.json" })
        {
            var settings = new ConfigurationBuilder().AddJsonFile(Path.Combine(api, file)).Build();
            Assert.False(DemoMode.IsEnabled(settings), file);
        }

        // Der Dienst läuft als Production und erwähnt den Demo-Modus nicht.
        var service = File.ReadAllText(RepoFile("dienst-einrichten.sh"));
        Assert.Contains("ASPNETCORE_ENVIRONMENT=Production", service);
        Assert.DoesNotContain("DemoMode", service);
        Assert.DoesNotContain("=Demo", service);
    }

    [Fact]
    public async Task Ohne_Demo_meldet_der_Banner_Endpunkt_nichts()
    {
        using var normal = new HomeControlApiFactory();
        using var client = normal.CreateClient();

        var info = await client.GetFromJsonAsync<DemoInfoResponse>("/api/demo");

        Assert.False(info!.DemoMode);
        Assert.Null(info.DemoEmail);
    }

    private static HostingEnvironment Environment(string name) => new() { EnvironmentName = name };

    private static IConfiguration Config(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    // Vom Ausgabeordner der Tests nach oben suchen, bis die Datei im Repo gefunden ist.
    private static string RepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Path.Exists(Path.Combine(dir.FullName, relativePath)))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return Path.Combine(dir.FullName, relativePath);
    }
}
