using HomeControl.Api.Models;
using Microsoft.AspNetCore.Authorization;

namespace HomeControl.Api.Auth;

// Namen der Berechtigungsregeln (Policies). Registriert werden sie in Program.cs,
// benutzt an den Endpunkten mit .RequireAuthorization(AuthPolicies.AdminOnly).
public static class AuthPolicies
{
    // Nur angemeldete Benutzer mit der Rolle Admin.
    // Ohne gültiges Token: 401. Angemeldet, aber kein Admin: 403.
    public const string AdminOnly = "AdminOnly";

    public static AuthorizationBuilder AddHomeControlPolicies(this AuthorizationBuilder builder) =>
        builder.AddPolicy(AdminOnly, policy => policy.RequireRole(Roles.Admin));
}
