using HomeControl.Api.Data;
using HomeControl.Api.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HomeControl.Api.Blink;

// Liest und schreibt die eine BlinkCredential-Zeile. Scoped wie der DbContext (siehe
// BlinkNetCameraClient, der als Singleton bei Bedarf einen eigenen Scope dafür aufmacht).
//
// Das Refresh-Token liegt nie im Klartext in der Datenbank: IDataProtector verschlüsselt es
// mit einem Schlüssel, der außerhalb des Repos liegt (siehe PersistKeysToFileSystem in
// Program.cs und docs/BLINK.md). Ohne diesen Schlüssel ist der Datenbankinhalt wertlos.
public class BlinkCredentialStore(
    ApplicationDbContext db,
    IDataProtectionProvider dataProtection,
    IOptionsMonitor<BlinkOptions> options)
{
    private const int RowId = 1;

    // Eigener "Zweck" (Purpose), damit dieser Schlüssel nicht versehentlich für etwas
    // anderes wiederverwendet wird, falls später weitere Data-Protection-Nutzer dazukommen.
    private readonly IDataProtector protector = dataProtection.CreateProtector("HomeControl.Blink.RefreshToken.v1");

    // Liefert HardwareId + entschlüsseltes Refresh-Token, oder null, wenn weder die
    // Datenbank noch der Bootstrap-Wert aus den User Secrets eines haben.
    public async Task<(string HardwareId, string RefreshToken)?> GetCredentialAsync(CancellationToken cancellationToken)
    {
        var row = await db.BlinkCredentials.FirstOrDefaultAsync(c => c.Id == RowId, cancellationToken);
        if (row is not null)
        {
            return (row.HardwareId, protector.Unprotect(row.EncryptedRefreshToken));
        }

        // Noch keine Datenbank-Zeile: beim allerersten Start den Bootstrap-Wert aus den
        // User Secrets übernehmen. Danach läuft alles nur noch über die Datenbank.
        var settings = options.CurrentValue;
        if (string.IsNullOrWhiteSpace(settings.HardwareId) || string.IsNullOrWhiteSpace(settings.InitialRefreshToken))
        {
            return null;
        }

        await SaveAsync(settings.HardwareId, settings.InitialRefreshToken, cancellationToken);
        return (settings.HardwareId, settings.InitialRefreshToken);
    }

    // Legt die Zeile an oder überschreibt sie. Wird auch vom OnTokenRefreshed-Ereignis aus
    // Blink.NET aufgerufen, sobald Blink während des Betriebs ein neues Token ausstellt.
    public async Task SaveAsync(string hardwareId, string refreshToken, CancellationToken cancellationToken)
    {
        var encrypted = protector.Protect(refreshToken);
        var row = await db.BlinkCredentials.FirstOrDefaultAsync(c => c.Id == RowId, cancellationToken);

        if (row is null)
        {
            db.BlinkCredentials.Add(new BlinkCredential
            {
                Id = RowId,
                HardwareId = hardwareId,
                EncryptedRefreshToken = encrypted,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
            });
        }
        else
        {
            row.HardwareId = hardwareId;
            row.EncryptedRefreshToken = encrypted;
            row.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
