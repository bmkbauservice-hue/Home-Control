using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;

namespace HomeControl.Api.Auth;

// Hilfsmethoden für den angemeldeten Benutzer (HttpContext.User bzw. ClaimsPrincipal im Endpunkt).
public static class ClaimsPrincipalExtensions
{
    // Liest die Benutzer-Id aus dem Claim "sub" des Access Tokens.
    // null, wenn sie fehlt (z. B. Anmeldung per API-Schlüssel des Agenten) oder keine Guid ist.
    // Wichtig: Wer anfragt, steht immer im Token, nie im Body. Sonst könnte sich jeder als
    // jemand anderes ausgeben.
    public static Guid? GetUserId(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var id) ? id : null;
}
