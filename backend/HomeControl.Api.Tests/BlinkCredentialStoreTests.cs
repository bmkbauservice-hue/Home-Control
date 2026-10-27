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
        new(db, dataProtection, new TestOptionsMonitor<BlinkOptions>(blinkOptions));

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
}
