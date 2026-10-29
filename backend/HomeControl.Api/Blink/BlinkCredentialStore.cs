using System.Security.Cryptography;
using System.Text;
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
// Program.cs und docs/BLINK.md). Ohne diesen Schlüssel ist der Datenbankinhalt wertlos –
// protector.Unprotect wirft dann eine CryptographicException (vom Aufrufer zu behandeln,
// siehe BlinkNetCameraClient).
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
    //
    // Steht in den User Secrets ein Blink:InitialRefreshToken, dessen Hash nicht zum zuletzt
    // übernommenen passt (z. B. weil tools/BlinkSpike nach einem fehlgeschlagenen Login
    // erneut gelaufen ist und ein neues Token gesetzt hat), wird es ohne Neustart übernommen –
    // auch wenn die Datenbank schon eine Zeile hat. Ein unveränderter Bootstrap-Wert
    // überschreibt dagegen NICHT ein inzwischen über OnTokenRefreshed rotiertes, neueres Token.
    public async Task<(string HardwareId, string RefreshToken)?> GetCredentialAsync(CancellationToken cancellationToken)
    {
        var row = await db.BlinkCredentials.FirstOrDefaultAsync(c => c.Id == RowId, cancellationToken);
        var settings = options.CurrentValue;
        var bootstrapToken = settings.InitialRefreshToken;

        var hasNewBootstrapToken =
            !string.IsNullOrWhiteSpace(bootstrapToken)
            && (row is null || Hash(bootstrapToken) != row.InitialRefreshTokenHash);

        if (hasNewBootstrapToken)
        {
            var hardwareId = string.IsNullOrWhiteSpace(settings.HardwareId)
                ? row?.HardwareId ?? string.Empty
                : settings.HardwareId;

            if (string.IsNullOrWhiteSpace(hardwareId))
            {
                return null;
            }

            await SaveBootstrapAsync(hardwareId, bootstrapToken!, cancellationToken);
            return (hardwareId, bootstrapToken!);
        }

        if (row is null)
        {
            return null;
        }

        return (row.HardwareId, protector.Unprotect(row.EncryptedRefreshToken));
    }

    // Legt die Zeile an oder überschreibt sie, OHNE den Bootstrap-Hash zu ändern. Wird vom
    // OnTokenRefreshed-Ereignis aus Blink.NET aufgerufen, sobald Blink während des Betriebs
    // ein neues Token ausstellt – das hat nichts mit den User Secrets zu tun.
    public Task SaveAsync(string hardwareId, string refreshToken, CancellationToken cancellationToken) =>
        SaveInternalAsync(hardwareId, refreshToken, updateBootstrapHash: false, cancellationToken);

    // Übernimmt einen (neuen) Bootstrap-Wert aus den User Secrets und merkt sich dessen Hash,
    // damit derselbe Wert nicht bei jedem Aufruf erneut als "neu" erkannt wird.
    private Task SaveBootstrapAsync(string hardwareId, string refreshToken, CancellationToken cancellationToken) =>
        SaveInternalAsync(hardwareId, refreshToken, updateBootstrapHash: true, cancellationToken);

    private async Task SaveInternalAsync(string hardwareId, string refreshToken, bool updateBootstrapHash, CancellationToken cancellationToken)
    {
        var encrypted = protector.Protect(refreshToken);
        var row = await db.BlinkCredentials.FirstOrDefaultAsync(c => c.Id == RowId, cancellationToken);

        if (row is null)
        {
            row = new BlinkCredential { Id = RowId };
            db.BlinkCredentials.Add(row);
        }

        row.HardwareId = hardwareId;
        row.EncryptedRefreshToken = encrypted;
        row.UpdatedAtUtc = DateTimeOffset.UtcNow;
        if (updateBootstrapHash)
        {
            row.InitialRefreshTokenHash = Hash(refreshToken);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
