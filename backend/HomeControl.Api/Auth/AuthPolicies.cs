using HomeControl.Api.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;

namespace HomeControl.Api.Auth;

// Namen der Berechtigungsregeln (Policies). Registriert werden sie in Program.cs,
// benutzt an den Endpunkten mit .RequireAuthorization(AuthPolicies.AdminOnly).
public static class AuthPolicies
{
    // Nur angemeldete Benutzer mit der Rolle Admin (JWT).
    // Ohne gültiges Token: 401. Angemeldet, aber kein Admin: 403.
    public const string AdminOnly = "AdminOnly";

    public static AuthorizationBuilder AddHomeControlPolicies(this AuthorizationBuilder builder) =>
        builder.AddPolicy(AdminOnly, policy => policy.RequireRole(Roles.Admin));

    // Regel für alles, was gelesen wird, sobald "Auth:RequireLoginForReading" an ist: angemeldet
    // per JWT (Frontend) ODER per API-Schlüssel (Agent). Sie gilt als Fallback für jeden Endpunkt
    // ohne eigene Regel; offen bleiben nur Endpunkte mit .AllowAnonymous() (z. B. login).
    public static AuthorizationPolicy CreateReadingPolicy() =>
        new AuthorizationPolicyBuilder(
                JwtBearerDefaults.AuthenticationScheme,
                ApiKeyDefaults.AuthenticationScheme)
            .RequireAuthenticatedUser()
            .Build();
}
