using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using HomeControl.Api.Models;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace HomeControl.Api.Auth;

// Erzeugt die beiden Tokens:
// - Access Token: ein JWT, vom Server signiert. Der Server muss sich nichts merken, er prüft
//   bei jeder Anfrage nur Signatur und Ablaufzeit.
// - Refresh-Token: eine lange Zufallszeichenkette ohne Inhalt. Der Server merkt sich ihren
//   Hash in der Datenbank und kann sie dadurch widerrufen.
public class TokenService(JwtOptions options, TimeProvider timeProvider)
{
    // Name des Claims für die Rolle im JWT. Kurz und lesbar, statt der langen
    // Microsoft-Bezeichnung "http://schemas.microsoft.com/ws/2008/06/identity/claims/role".
    public const string RoleClaim = "role";

    // Erstellt ein signiertes JWT mit genau drei Angaben über den Benutzer: Id, E-Mail, Rolle.
    // Kein Passwort, kein Hash, keine Zugangsdaten zu Tuya oder Blink: Ein JWT ist nur
    // signiert, nicht verschlüsselt. Jeder, der es hat, kann den Inhalt lesen (z. B. auf jwt.io).
    public AccessToken CreateAccessToken(User user)
    {
        var now = timeProvider.GetUtcNow();
        var expiresAt = now.AddMinutes(options.AccessTokenMinutes);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = options.Issuer,
            Audience = options.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                new Claim(RoleClaim, user.Role),
            }),
            // HS256: Signatur mit einem geheimen Schlüssel, den nur der Server kennt.
            SigningCredentials = new SigningCredentials(CreateSigningKey(options), SecurityAlgorithms.HmacSha256),
        };

        var token = new JsonWebTokenHandler().CreateToken(descriptor);
        return new AccessToken(token, expiresAt);
    }

    // 64 Byte aus dem kryptografischen Zufallsgenerator, als Base64Url-Text (nur Buchstaben,
    // Ziffern, "-" und "_", damit er ohne Umwandlung in ein Cookie passt).
    // Nicht Random oder Guid.NewGuid(): Die sind nicht für Sicherheitszwecke gedacht.
    public static string CreateRefreshToken() =>
        Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(64));

    // SHA-256 des Refresh-Tokens als Hex-Text. Nur dieser Hash kommt in die Datenbank.
    public static string HashRefreshToken(string refreshToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)));

    // Regeln, nach denen der Server ein eingehendes Access Token prüft. Wird in Program.cs für
    // die JWT-Bearer-Middleware und in den Tests verwendet, damit beide dasselbe prüfen.
    public static TokenValidationParameters CreateValidationParameters(JwtOptions options) => new()
    {
        ValidateIssuer = true,
        ValidIssuer = options.Issuer,
        ValidateAudience = true,
        ValidAudience = options.Audience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = CreateSigningKey(options),
        ValidateLifetime = true,
        // Erlaubte Abweichung der Uhren. Standard wären 5 Minuten, dann gälte ein
        // 15-Minuten-Token in Wahrheit 20 Minuten.
        ClockSkew = TimeSpan.FromSeconds(30),
        // Welche Claims als Name und als Rolle gelten (für User.Identity.Name und IsInRole).
        NameClaimType = JwtRegisteredClaimNames.Email,
        RoleClaimType = RoleClaim,
    };

    private static SymmetricSecurityKey CreateSigningKey(JwtOptions options) =>
        new(Encoding.UTF8.GetBytes(options.SigningKey));
}

// Das fertige Access Token und wann es abläuft (das Frontend zeigt das nicht an, kann es aber
// zum Debuggen nutzen).
public record AccessToken(string Token, DateTimeOffset ExpiresAtUtc);
