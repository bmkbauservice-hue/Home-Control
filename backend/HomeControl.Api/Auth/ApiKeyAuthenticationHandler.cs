using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace HomeControl.Api.Auth;

public static class ApiKeyDefaults
{
    // Name dieses Anmeldeverfahrens (Scheme), neben "Bearer" für die JWT-Tokens.
    public const string AuthenticationScheme = "ApiKey";

    public const string HeaderName = "X-Api-Key";
}

// Prüft den Header "X-Api-Key" gegen den Schlüssel aus den Einstellungen.
//
// Wer den Schlüssel kennt, darf LESEN (wie ein angemeldeter Benutzer). Schreibende Endpunkte
// prüfen ausschließlich das JWT mit der Rolle Admin, der Schlüssel öffnet sie nie.
public class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IOptionsMonitor<AgentOptions> agentOptions)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Kein Header: Dieses Verfahren ist nicht zuständig, ein anderes (JWT) darf es versuchen.
        if (!Request.Headers.TryGetValue(ApiKeyDefaults.HeaderName, out var provided))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var configured = agentOptions.CurrentValue.ApiKey;
        if (configured.Length < AgentOptions.MinimumKeyLength)
        {
            return Task.FromResult(AuthenticateResult.Fail("Der Zugang für den Agenten ist nicht eingerichtet."));
        }

        if (provided.Count != 1 || !KeysMatch(provided.ToString(), configured))
        {
            return Task.FromResult(AuthenticateResult.Fail("Ungültiger API-Schlüssel."));
        }

        var identity = new ClaimsIdentity(
            [new Claim("sub", "agent"), new Claim("name", "Agent"), new Claim("role", "Agent")],
            Scheme.Name,
            nameType: "name",
            roleType: "role");
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    // Vergleich in konstanter Zeit: Ein normaler Textvergleich hört beim ersten falschen Zeichen
    // auf, die Antwortzeit würde verraten, wie viele Zeichen schon stimmen. Beide Seiten werden
    // vorher gehasht, damit sie gleich lang sind und die Länge nichts verrät.
    private static bool KeysMatch(string provided, string configured) =>
        CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(provided)),
            SHA256.HashData(Encoding.UTF8.GetBytes(configured)));
}
