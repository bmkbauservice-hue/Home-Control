using System.Security.Cryptography;
using HomeControl.Api.Blink;
using HomeControl.Api.Data;
using HomeControl.Api.Tests.Fakes;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HomeControl.Api.Tests;

// Tests für BlinkCredentialStore: Verschlüsselung, Übernahme des Bootstrap-Werts aus den
// User Secrets, Überschreiben bei einem neuen Token. Keine echte Anfrage an Blink.
// EphemeralDataProtectionProvider verschlüsselt nur für die Laufzeit des Tests,
// genau wie die echte Data Protection API, nur ohne Schlüssel auf der Festplatte.
public class BlinkCredentialStoreTests : IDisposable
{
    private readonly SqliteConnection connection;
    private readonly ApplicationDbContext db;
    private readonly IDataProtectionProvider dataProtection = new EphemeralDataProtectionProvider();

    public BlinkCredentialStoreTests()
    {
        connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;
        db = new ApplicationDbContext(options);
        db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        db.Dispose();
        connection.Dispose();
    }

    private BlinkCredentialStore CreateStore(BlinkOptions blinkOptions) =>
        CreateStore(new TestOptionsMonitor<BlinkOptions>(blinkOptions));

    private BlinkCredentialStore CreateStore(TestOptionsMonitor<BlinkOptions> monitor) =>
        new(db, dataProtection, monitor);

    [Fact]
    public async Task GetCredentialAsync_ohne_DB_Zeile_und_ohne_Bootstrap_liefert_null()
    {
        var store = CreateStore(new BlinkOptions());

        var result = await store.GetCredentialAsync(CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetCredentialAsync_uebernimmt_Bootstrap_Wert_beim_ersten_Mal()
    {
        var store = CreateStore(new BlinkOptions { HardwareId = "hw-1", InitialRefreshToken = "rt-1" });

        var result = await store.GetCredentialAsync(CancellationToken.None);

        Assert.Equal(("hw-1", "rt-1"), result);
        var row = Assert.Single(db.BlinkCredentials);
        // Nie im Klartext in der Datenbank.
        Assert.NotEqual("rt-1", row.EncryptedRefreshToken);
    }

    [Fact]
    public async Task GetCredentialAsync_liest_aus_der_DB_wenn_schon_eine_Zeile_existiert()
    {
        var bootstrapStore = CreateStore(new BlinkOptions { HardwareId = "hw-1", InitialRefreshToken = "rt-1" });
        await bootstrapStore.GetCredentialAsync(CancellationToken.None);

        // Neuer Store ohne Bootstrap-Wert muss trotzdem die DB-Zeile finden.
        var store = CreateStore(new BlinkOptions());
        var result = await store.GetCredentialAsync(CancellationToken.None);

        Assert.Equal(("hw-1", "rt-1"), result);
    }

    [Fact]
    public async Task SaveAsync_ueberschreibt_ein_vorhandenes_Token()
    {
        var store = CreateStore(new BlinkOptions());
        await store.SaveAsync("hw-1", "rt-1", CancellationToken.None);
        await store.SaveAsync("hw-1", "rt-2", CancellationToken.None);

        var result = await store.GetCredentialAsync(CancellationToken.None);

        Assert.Equal(("hw-1", "rt-2"), result);
        Assert.Single(db.BlinkCredentials);
    }

    // Deckt Punkt 1 aus dem Review ab: Nach einem fehlgeschlagenen Login setzt tools/BlinkSpike
    // einen NEUEN Blink:InitialRefreshToken in die User Secrets. Ohne Neustart muss der nächste
    // Aufruf diesen neuen Wert erkennen (anderer Hash) und übernehmen.
    [Fact]
    public async Task GetCredentialAsync_uebernimmt_neuen_Bootstrap_Wert_ohne_Neustart()
    {
        var monitor = new TestOptionsMonitor<BlinkOptions>(new BlinkOptions { HardwareId = "hw-1", InitialRefreshToken = "rt-1" });
        var store = CreateStore(monitor);
        await store.GetCredentialAsync(CancellationToken.None);

        // Simuliert: tools/BlinkSpike lief erneut, neuer Wert in den User Secrets.
        monitor.CurrentValue = new BlinkOptions { HardwareId = "hw-1", InitialRefreshToken = "rt-2" };
        var result = await store.GetCredentialAsync(CancellationToken.None);

        Assert.Equal(("hw-1", "rt-2"), result);
    }

    // Ein unveränderter Bootstrap-Wert darf ein zwischenzeitlich über OnTokenRefreshed
    // rotiertes, neueres Token NICHT überschreiben.
    [Fact]
    public async Task GetCredentialAsync_ueberschreibt_rotiertes_Token_nicht_mit_unveraendertem_Bootstrap_Wert()
    {
        var monitor = new TestOptionsMonitor<BlinkOptions>(new BlinkOptions { HardwareId = "hw-1", InitialRefreshToken = "rt-1" });
        var store = CreateStore(monitor);
        await store.GetCredentialAsync(CancellationToken.None);

        // Blink hat während des Betriebs ein neues Token ausgestellt (OnTokenRefreshed).
        await store.SaveAsync("hw-1", "rt-live", CancellationToken.None);

        // Die User Secrets wurden nicht angefasst (immer noch "rt-1").
        var result = await store.GetCredentialAsync(CancellationToken.None);

        Assert.Equal(("hw-1", "rt-live"), result);
    }

    // Deckt Punkt 4 aus dem Review ab: Ohne den passenden Data-Protection-Schlüssel wirft
    // Unprotect eine CryptographicException. BlinkCredentialStore fängt sie bewusst NICHT ab
    // (siehe Kommentar dort) – BlinkNetCameraClient wandelt sie in "Neu-Login nötig" um.
    [Fact]
    public async Task GetCredentialAsync_wirft_CryptographicException_bei_falschem_Schluessel()
    {
        var monitor = new TestOptionsMonitor<BlinkOptions>(new BlinkOptions { HardwareId = "hw-1", InitialRefreshToken = "rt-1" });
        var writingStore = new BlinkCredentialStore(db, new EphemeralDataProtectionProvider(), monitor);
        await writingStore.GetCredentialAsync(CancellationToken.None);

        // Kein neuer Bootstrap-Wert mehr, der nächste Aufruf liest aus der Datenbank.
        monitor.CurrentValue = new BlinkOptions();
        // Anderer Schlüssel, z. B. nach einem Umzug ohne die Schlüssel-Dateien.
        var readingStore = new BlinkCredentialStore(db, new EphemeralDataProtectionProvider(), monitor);

        await Assert.ThrowsAsync<CryptographicException>(() => readingStore.GetCredentialAsync(CancellationToken.None));
    }
}
