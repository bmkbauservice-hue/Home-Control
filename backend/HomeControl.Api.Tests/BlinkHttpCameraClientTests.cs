using System.Net;
using System.Text;
using HomeControl.Api.Blink;
using HomeControl.Api.Data;
using HomeControl.Api.Tests.Fakes;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HomeControl.Api.Tests;

// Tests für den eigenen Blink-Client: Token-Erneuerung (eine gleichzeitig, sofort verschlüsselt
// gespeichert, nie im Log), einmal erneuern bei 401 statt Schleife, 426 ist kein Login-Problem.
// Blink spielt FakeBlinkHandler, die Datenbank ist SQLite im Arbeitsspeicher.
public class BlinkHttpCameraClientTests : IDisposable
{
    private const string StartToken = "rt-start-aus-spike";

    private readonly SqliteConnection connection = new("DataSource=:memory:");
    private readonly FakeBlinkHandler blink = new();
    private readonly FakeTimeProvider clock = new();
    private readonly CapturingLoggerProvider logs = new();
    private readonly TestOptionsMonitor<BlinkOptions> options =
        new(new BlinkOptions { HardwareId = "HW-TEST", InitialRefreshToken = StartToken });
    private readonly ServiceProvider services;
    private readonly BlinkHttpCameraClient client;

    // Liefert immer einen HttpClient mit dem FakeBlinkHandler (statt ins Internet).
    private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    public BlinkHttpCameraClientTests()
    {
        connection.Open();
        var collection = new ServiceCollection();
        collection.AddLogging(l => l.AddProvider(logs).SetMinimumLevel(LogLevel.Debug));
        collection.AddDbContext<ApplicationDbContext>(o => o.UseSqlite(connection));
        // Ein Schlüssel für alle Scopes, sonst ließe sich das Token im nächsten Scope nicht entschlüsseln.
        collection.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        collection.AddSingleton<IOptionsMonitor<BlinkOptions>>(options);
        collection.AddScoped<BlinkCredentialStore>();
        services = collection.BuildServiceProvider();

        using (var scope = services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.EnsureCreated();
        }

        client = new BlinkHttpCameraClient(
            new FakeHttpClientFactory(blink),
            services.GetRequiredService<IServiceScopeFactory>(),
            options,
            clock,
            services.GetRequiredService<ILogger<BlinkHttpCameraClient>>())
        {
            // Im Test nicht wirklich eine Sekunde zwischen den Befehlsabfragen warten.
            CommandPollInterval = TimeSpan.Zero,
            ClipPollInterval = TimeSpan.Zero,
        };
    }

    private void UseActiveLocalStorage(string status = "active") =>
        blink.HomescreenResponse = _ => (HttpStatusCode.OK,
            $$"""{ "networks": [], "cameras": [], "sync_modules": [ { "id": 5, "network_id": 1, "local_storage_enabled": {{(status == "active" ? "true" : "false")}}, "local_storage_status": "{{status}}" } ] }""");

    [Fact]
    public async Task Lokale_Clips_kommen_aus_dem_Manifest()
    {
        UseActiveLocalStorage();

        var list = await client.GetClipsAsync(false, CancellationToken.None);

        var clip = Assert.Single(list.Clips);
        Assert.Equal("l11", clip.Key);
        Assert.Equal("Flur", clip.CameraName);
        Assert.Equal(new DateTimeOffset(2026, 11, 8, 22, 55, 13, TimeSpan.Zero), clip.CreatedAtUtc);
        Assert.Null(list.Problem);
    }

    [Fact]
    public async Task Clip_wird_als_MP4_geladen_und_nichts_wird_geloescht()
    {
        UseActiveLocalStorage();
        await client.GetClipsAsync(false, CancellationToken.None);

        var video = await client.GetClipVideoAsync("l11", CancellationToken.None);

        Assert.NotNull(video);
        Assert.True(BlinkHttpCameraClient.IsMp4(video.Content));
        Assert.DoesNotContain(blink.LocalStorageCalls, call => call.Contains("delete", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(blink.LocalStorageCalls, call => call.StartsWith("POST", StringComparison.Ordinal) && call.EndsWith("/manifest/900/clip/request/11", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Ohne_MP4_gibt_es_keinen_Clip()
    {
        UseActiveLocalStorage();
        blink.ClipResponse = () => (HttpStatusCode.OK, Encoding.UTF8.GetBytes("<html>keine Datei</html>"));
        await client.GetClipsAsync(false, CancellationToken.None);

        await Assert.ThrowsAsync<BlinkApiException>(() => client.GetClipVideoAsync("l11", CancellationToken.None));
    }

    [Fact]
    public async Task Ohne_aktiven_Speicher_wird_das_Problem_genannt()
    {
        UseActiveLocalStorage("inactive");

        var list = await client.GetClipsAsync(false, CancellationToken.None);

        Assert.Empty(list.Clips);
        Assert.NotNull(list.Problem);
        Assert.Empty(blink.LocalStorageCalls);
    }

    [Fact]
    public async Task Clip_Liste_weckt_das_Sync_Module_nur_selten()
    {
        UseActiveLocalStorage();

        await client.GetClipsAsync(false, CancellationToken.None);
        var afterFirst = blink.LocalStorageCalls.Count;
        await client.GetClipsAsync(false, CancellationToken.None);
        Assert.Equal(afterFirst, blink.LocalStorageCalls.Count);

        await client.GetClipsAsync(true, CancellationToken.None);
        Assert.True(blink.LocalStorageCalls.Count > afterFirst);
    }

    public void Dispose()
    {
        services.Dispose();
        connection.Dispose();
    }

    private async Task<(string HardwareId, string RefreshToken)?> StoredCredentialAsync()
    {
        using var scope = services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<BlinkCredentialStore>().GetCredentialAsync(CancellationToken.None);
    }

    private static (HttpStatusCode, string) Status(HttpStatusCode code) => (code, "{}");

    // ---------- Normalfall ----------

    [Fact]
    public async Task Kameras_kommen_aus_dem_Homescreen()
    {
        var cameras = await client.GetCamerasAsync(CancellationToken.None);

        Assert.Equal(["101", "102", "201"], cameras.Select(c => c.Id));
        Assert.Equal(["Garten", "Einfahrt", "Flur Mini"], cameras.Select(c => c.Name));
        Assert.Equal("ok", cameras[0].Battery);
        Assert.Equal("low", cameras[1].Battery);
    }

    [Fact]
    public async Task Homescreen_ohne_alten_App_Build_Header_mit_Bearer_Token()
    {
        await client.GetCamerasAsync(CancellationToken.None);

        var request = Assert.Single(blink.HomescreenRequests);
        Assert.Equal("https://rest-e006.immedia-semi.com/api/v3/accounts/4711/homescreen", request.RequestUri!.ToString());
        Assert.Equal("access-1", request.Headers.Authorization?.Parameter);
        Assert.False(request.Headers.Contains("APP_BUILD"));
        Assert.False(request.Headers.Contains("APP-BUILD"));
        // Erneuert wurde mit dem Startwert und der HardwareId.
        Assert.Contains($"refresh_token={StartToken}", blink.TokenRequestBodies[0]);
        Assert.Contains("hardware_id=HW-TEST", blink.TokenRequestBodies[0]);
    }

    [Fact]
    public async Task Status_meldet_connected()
    {
        var status = await client.GetStatusAsync(CancellationToken.None);

        Assert.Equal(BlinkConnectionState.Connected, status.State);
        Assert.Equal(clock.GetUtcNow(), status.LastSuccessfulFetchUtc);
    }

    // ---------- Token: sofort speichern, nie loggen, nur eine Erneuerung ----------

    [Fact]
    public async Task Neues_Refresh_Token_wird_sofort_verschluesselt_gespeichert_und_nie_geloggt()
    {
        await client.GetCamerasAsync(CancellationToken.None);

        // In der Datenbank steht das rotierte Token, verschlüsselt.
        Assert.Equal("refresh-1", (await StoredCredentialAsync())!.Value.RefreshToken);
        using (var scope = services.CreateScope())
        {
            var row = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().BlinkCredentials.Single();
            Assert.DoesNotContain("refresh-1", row.EncryptedRefreshToken);
        }

        // Kein Token, keine Bild-Adresse, keine Seriennummer im Log.
        Assert.DoesNotContain(logs.Entries, e =>
            e.Message.Contains("refresh-1") || e.Message.Contains("access-1") || e.Message.Contains(StartToken)
            || e.Message.Contains("GEHEIM"));
        // Aber die Feldnamen für die Prüfung an echten Daten.
        Assert.Contains(logs.Entries, e => e.Message.Contains("Felder je Eintrag") && e.Message.Contains("thumbnail"));
    }

    [Fact]
    public async Task Nur_eine_Erneuerung_gleichzeitig()
    {
        // Mehrere Anfragen auf einmal, noch ohne Token: Es darf nur EINMAL erneuert werden.
        var calls = Enumerable.Range(0, 5).Select(i => i % 2 == 0
            ? client.GetCamerasAsync(CancellationToken.None)
            : client.GetStatusAsync(CancellationToken.None).ContinueWith(_ => (IReadOnlyList<BlinkCameraInfo>)[]));
        await Task.WhenAll(calls);

        Assert.Single(blink.TokenRequestBodies);
        Assert.Single(blink.HomescreenRequests);
    }

    [Fact]
    public async Task Innerhalb_von_2_Minuten_kein_neuer_Abruf_danach_ohne_neues_Token()
    {
        await client.GetCamerasAsync(CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(119));
        await client.GetCamerasAsync(CancellationToken.None);
        Assert.Single(blink.HomescreenRequests);

        clock.Advance(TimeSpan.FromSeconds(1));
        await client.GetCamerasAsync(CancellationToken.None);

        Assert.Equal(2, blink.HomescreenRequests.Count);
        // Das Access Token gilt eine Stunde, es wird nicht neu geholt.
        Assert.Single(blink.TokenRequestBodies);
    }

    // ---------- 401: einmal erneuern, dann Fehler statt Schleife ----------

    [Fact]
    public async Task Bei_401_wird_einmal_erneuert_und_wiederholt()
    {
        blink.HomescreenResponse = n => n == 1 ? Status(HttpStatusCode.Unauthorized) : (HttpStatusCode.OK, BlinkJson.Homescreen());

        var cameras = await client.GetCamerasAsync(CancellationToken.None);

        Assert.Equal(3, cameras.Count);
        Assert.Equal(2, blink.TokenRequestBodies.Count);
        // Die zweite Erneuerung benutzt das rotierte Token der ersten.
        Assert.Contains("refresh_token=refresh-1", blink.TokenRequestBodies[1]);
    }

    [Fact]
    public async Task Zweimal_401_ergibt_loginRequired_ohne_Schleife()
    {
        blink.HomescreenResponse = _ => Status(HttpStatusCode.Unauthorized);
        // Kein neues Refresh-Token: Es bleibt der Startwert.
        blink.TokenResponse = n => (HttpStatusCode.OK, BlinkJson.Token($"access-{n}", null, 3600));

        await Assert.ThrowsAsync<BlinkLoginRequiredException>(() => client.GetCamerasAsync(CancellationToken.None));
        var status = await client.GetStatusAsync(CancellationToken.None);

        Assert.Equal(BlinkConnectionState.LoginRequired, status.State);
        // Genau zwei Erneuerungen beim ersten Aufruf, beim zweiten keine mehr (gleiches, abgelehntes Token).
        Assert.Equal(2, blink.TokenRequestBodies.Count);
        Assert.Equal(2, blink.HomescreenRequests.Count);
    }

    [Fact]
    public async Task Abgelehntes_Refresh_Token_wird_nicht_wiederholt_neuer_Startwert_schon()
    {
        blink.TokenResponse = _ => Status(HttpStatusCode.Unauthorized);

        await Assert.ThrowsAsync<BlinkLoginRequiredException>(() => client.GetCamerasAsync(CancellationToken.None));
        await Assert.ThrowsAsync<BlinkLoginRequiredException>(() => client.GetCamerasAsync(CancellationToken.None));
        Assert.Single(blink.TokenRequestBodies);

        // tools/BlinkSpike hat einen neuen Startwert eingetragen: Jetzt wird es ohne Neustart neu versucht.
        options.CurrentValue = new BlinkOptions { HardwareId = "HW-TEST", InitialRefreshToken = "rt-neu-aus-spike" };
        blink.TokenResponse = n => (HttpStatusCode.OK, BlinkJson.Token($"access-{n}", $"refresh-{n}", 3600));

        var cameras = await client.GetCamerasAsync(CancellationToken.None);

        Assert.Equal(3, cameras.Count);
        Assert.Contains("refresh_token=rt-neu-aus-spike", blink.TokenRequestBodies[1]);
    }

    // ---------- "Neues Bild" (bewusste Ausnahme von nur lesen) ----------

    [Fact]
    public async Task Neues_Bild_loest_POST_aus_wartet_auf_den_Befehl_und_holt_das_neue_Bild()
    {
        const long newTs = 1793700000;
        blink.HomescreenResponse = n => n == 1
            ? (HttpStatusCode.OK, BlinkJson.Homescreen())
            : (HttpStatusCode.OK, BlinkJson.Homescreen().Replace(
                "\"thumbnail\": \"/media/e006/account/4711/network/1/camera/101/clip_abc\"", $"\"thumbnail\": \"{newTs}\""));

        var result = await client.RequestNewThumbnailAsync("101", CancellationToken.None);

        var request = Assert.Single(blink.SnapshotRequests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://rest-e006.immedia-semi.com/network/1/camera/101/thumbnail", request.RequestUri!.ToString());
        Assert.Equal("access-1", request.Headers.Authorization?.Parameter);
        Assert.Equal(1, blink.CommandCount);
        // Danach wurde der Homescreen neu geholt und die neue Aufnahmezeit übernommen.
        Assert.Equal(2, blink.HomescreenRequests.Count);
        Assert.True(result!.Completed);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(newTs), result.ThumbnailTakenAtUtc);
    }

    [Fact]
    public async Task Neues_Bild_fuer_eine_Mini_nimmt_die_owls_Adresse()
    {
        await client.RequestNewThumbnailAsync("201", CancellationToken.None);

        Assert.Equal("https://rest-e006.immedia-semi.com/api/v1/accounts/4711/networks/1/owls/201/thumbnail",
            Assert.Single(blink.SnapshotRequests).RequestUri!.ToString());
    }

    [Fact]
    public async Task Neues_Bild_gibt_nach_40_Abfragen_auf()
    {
        blink.CommandResponse = _ => (HttpStatusCode.OK, """{ "complete": false, "status_code": 908 }""");

        var result = await client.RequestNewThumbnailAsync("101", CancellationToken.None);

        Assert.False(result!.Completed);
        Assert.Equal(40, blink.CommandCount);
    }

    [Fact]
    public async Task Neues_Bild_fuer_unbekannte_Kamera_loest_nichts_aus()
    {
        Assert.Null(await client.RequestNewThumbnailAsync("999", CancellationToken.None));
        Assert.Empty(blink.SnapshotRequests);
    }

    // ---------- Ein Prozess pro Datenbank ----------

    private BlinkHttpCameraClient CreateSecondProcess() => new(
        new FakeHttpClientFactory(blink),
        services.GetRequiredService<IServiceScopeFactory>(),
        options,
        clock,
        services.GetRequiredService<ILogger<BlinkHttpCameraClient>>());

    [Fact]
    public async Task Vor_der_Erneuerung_wird_das_neuere_Token_aus_der_Datenbank_uebernommen()
    {
        // "Prozess A" (der Dienst) erneuert: refresh-1 steht in der Datenbank.
        await client.GetCamerasAsync(CancellationToken.None);

        // "Prozess B" (z. B. app-starten.sh) auf derselben Datenbank nimmt refresh-1, nicht den alten Startwert.
        var second = CreateSecondProcess();
        await second.GetCamerasAsync(CancellationToken.None);
        Assert.Contains("refresh_token=refresh-1", blink.TokenRequestBodies[1]);

        // A muss später erneuern (Access Token abgelaufen) und nimmt das von B rotierte refresh-2 aus der DB,
        // nicht sein eigenes, inzwischen ungültiges refresh-1 aus dem Arbeitsspeicher.
        clock.Advance(TimeSpan.FromHours(1));
        await client.GetCamerasAsync(CancellationToken.None);
        Assert.Contains("refresh_token=refresh-2", blink.TokenRequestBodies[2]);
    }

    [Fact]
    public async Task Ausgeschaltet_fragt_nichts_ab()
    {
        options.CurrentValue = new BlinkOptions { HardwareId = "HW-TEST", InitialRefreshToken = StartToken, Enabled = false };

        var status = await client.GetStatusAsync(CancellationToken.None);

        Assert.Equal(BlinkConnectionState.Disabled, status.State);
        await Assert.ThrowsAsync<BlinkDisabledException>(() => client.GetCamerasAsync(CancellationToken.None));
        Assert.Empty(blink.TokenRequestBodies);
        Assert.Empty(blink.HomescreenRequests);
    }

    // ---------- Vorschaubilder ----------

    [Fact]
    public async Task Online_nur_bei_eindeutigem_Status_und_Bild_Infos_in_der_Liste()
    {
        var cameras = await client.GetCamerasAsync(CancellationToken.None);

        Assert.Equal([true, false, null], cameras.Select(c => c.Online));
        Assert.Equal([true, false, true], cameras.Select(c => c.HasThumbnail));
        Assert.Null(cameras[0].ThumbnailTakenAtUtc);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(BlinkJson.ThumbnailTs), cameras[2].ThumbnailTakenAtUtc);
    }

    [Fact]
    public async Task Altes_Bildformat_Pfad_plus_jpg_mit_unserem_Token()
    {
        var thumbnail = await client.GetThumbnailAsync("101", CancellationToken.None);

        Assert.Equal(BlinkJson.FakeJpeg, thumbnail!.Content);
        Assert.Equal("image/jpeg", thumbnail.ContentType);
        Assert.Null(thumbnail.TakenAtUtc);
        var request = Assert.Single(blink.ImageRequests);
        Assert.Equal("https://rest-e006.immedia-semi.com/media/e006/account/4711/network/1/camera/101/clip_abc.jpg",
            request.RequestUri!.ToString());
        Assert.Equal(HttpMethod.Get, request.Method);   // nur lesen, nie ein neues Foto (POST)
        Assert.Equal("access-1", request.Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task Neues_Bildformat_aus_Zeitstempel_mit_Aufnahmezeit()
    {
        var thumbnail = await client.GetThumbnailAsync("201", CancellationToken.None);

        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(BlinkJson.ThumbnailTs), thumbnail!.TakenAtUtc);
        Assert.Equal(
            $"https://rest-e006.immedia-semi.com/api/v3/media/accounts/4711/networks/1/owl/201/thumbnail/thumbnail.jpg?ts={BlinkJson.ThumbnailTs}&ext=",
            Assert.Single(blink.ImageRequests).RequestUri!.ToString());
    }

    [Fact]
    public async Task Verdaechtiger_Pfad_und_unbekannte_Kamera_ergeben_kein_Bild_und_keine_Anfrage()
    {
        Assert.Null(await client.GetThumbnailAsync("102", CancellationToken.None));
        Assert.Null(await client.GetThumbnailAsync("999", CancellationToken.None));
        Assert.Empty(blink.ImageRequests);
    }

    [Fact]
    public async Task Bild_wird_60_Sekunden_vorgehalten()
    {
        await client.GetThumbnailAsync("101", CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(59));
        await client.GetThumbnailAsync("101", CancellationToken.None);
        Assert.Single(blink.ImageRequests);

        clock.Advance(TimeSpan.FromSeconds(1));
        await client.GetThumbnailAsync("101", CancellationToken.None);
        Assert.Equal(2, blink.ImageRequests.Count);
    }

    [Fact]
    public async Task Bild_bei_401_einmal_erneuern_dann_Fehler()
    {
        blink.ImageResponse = n => n == 1
            ? (HttpStatusCode.Unauthorized, [], "application/json")
            : (HttpStatusCode.OK, BlinkJson.FakeJpeg, "image/jpeg");
        Assert.NotNull(await client.GetThumbnailAsync("101", CancellationToken.None));
        Assert.Equal(2, blink.TokenRequestBodies.Count);

        blink.ImageResponse = _ => (HttpStatusCode.Unauthorized, [], "application/json");
        clock.Advance(TimeSpan.FromSeconds(60));
        await Assert.ThrowsAsync<BlinkLoginRequiredException>(() => client.GetThumbnailAsync("101", CancellationToken.None));
    }

    [Fact]
    public async Task Keine_Bilddaten_von_Blink_ergeben_einen_Fehler()
    {
        blink.ImageResponse = _ => (HttpStatusCode.OK, "<html>Fehler</html>"u8.ToArray(), "text/html");

        await Assert.ThrowsAsync<BlinkApiException>(() => client.GetThumbnailAsync("101", CancellationToken.None));
    }

    [Fact]
    public async Task Form_des_Bildpfads_ohne_Ziffern_im_Log()
    {
        await client.GetCamerasAsync(CancellationToken.None);

        Assert.Contains(logs.Entries, e => e.Message.Contains("Form des Bildpfads /media/e#/account/#/network/#/camera/#/clip_abc"));
        Assert.DoesNotContain(logs.Entries, e => e.Message.Contains("4711"));
    }

    // ---------- Andere Fehler ----------

    [Fact]
    public async Task Upgrade_Required_ist_kein_Login_Problem()
    {
        blink.HomescreenResponse = _ => (HttpStatusCode.UpgradeRequired, """{"message":"An app update is required"}""");

        var error = await Assert.ThrowsAsync<BlinkApiException>(() => client.GetCamerasAsync(CancellationToken.None));
        Assert.Contains("426", error.Message);

        // Die Sitzung bleibt gültig: Der nächste Versuch holt KEIN neues Token.
        blink.HomescreenResponse = _ => (HttpStatusCode.OK, BlinkJson.Homescreen());
        await client.GetCamerasAsync(CancellationToken.None);
        Assert.Single(blink.TokenRequestBodies);
    }

    [Fact]
    public async Task Seltsamer_Tier_fuehrt_nie_zu_einem_fremden_Server()
    {
        blink.TierInfoResponse = () => (HttpStatusCode.OK, """{ "tier": "evil.example.com/x", "account_id": 4711 }""");

        await Assert.ThrowsAsync<BlinkApiException>(() => client.GetCamerasAsync(CancellationToken.None));

        Assert.Empty(blink.HomescreenRequests);
    }

    [Fact]
    public async Task Ohne_HardwareId_nicht_eingerichtet_und_keine_Anfrage()
    {
        options.CurrentValue = new BlinkOptions();

        var status = await client.GetStatusAsync(CancellationToken.None);

        Assert.Equal(BlinkConnectionState.NotConfigured, status.State);
        Assert.Empty(blink.TokenRequestBodies);
    }
}
