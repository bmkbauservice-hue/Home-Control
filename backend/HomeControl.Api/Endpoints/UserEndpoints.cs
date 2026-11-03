using System.Security.Claims;
using HomeControl.Api.Auth;
using HomeControl.Api.Contracts;

namespace HomeControl.Api.Endpoints;

public static class UserEndpoints
{
    // Benutzer verwalten, nur Admin. Anlegen bleibt POST /api/auth/register (mit Rolle).
    public static void MapUserEndpoints(this WebApplication app)
    {
        var users = app.MapGroup("/api/users").WithTags("Benutzer").RequireAuthorization(AuthPolicies.AdminOnly);

        users.MapGet("/", GetUsers).WithSummary("Alle Benutzer mit Rolle (nur Admin)");
        users.MapPut("/{id:guid}/role", ChangeRole).WithSummary("Rolle ändern (nur Admin, nicht sich selbst, nie den letzten Admin)");
        users.MapDelete("/{id:guid}", DeleteUser).WithSummary("Benutzer löschen (nur Admin, nicht sich selbst, nie den letzten Admin)");
    }

    private static async Task<IResult> GetUsers(UserAdminService service, CancellationToken cancellationToken)
    {
        var users = await service.ListAsync(cancellationToken);
        return Results.Ok(users.Select(u => new UserListResponse(u.Id, u.Email, u.Role, u.CreatedAtUtc, u.LockedOut)));
    }

    private static async Task<IResult> ChangeRole(
        Guid id,
        ChangeRoleRequest request,
        ClaimsPrincipal caller,
        UserAdminService service,
        CancellationToken cancellationToken) =>
        ToResult(await service.ChangeRoleAsync(id, request.Role, caller.GetUserId()!.Value, cancellationToken));

    private static async Task<IResult> DeleteUser(
        Guid id,
        ClaimsPrincipal caller,
        UserAdminService service,
        CancellationToken cancellationToken) =>
        ToResult(await service.DeleteAsync(id, caller.GetUserId()!.Value, cancellationToken));

    // 409 Conflict: Die Anfrage ist in Ordnung, passt aber nicht zum aktuellen Zustand (letzter Admin, man selbst).
    private static IResult ToResult(UserChangeStatus status) => status switch
    {
        UserChangeStatus.Done => Results.NoContent(),
        UserChangeStatus.NotFound => Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Diesen Benutzer gibt es nicht."),
        UserChangeStatus.LastAdmin => Results.Problem(statusCode: StatusCodes.Status409Conflict,
            title: "Das ist der letzte Admin. Erst einen anderen Benutzer zum Admin machen."),
        _ => Results.Problem(statusCode: StatusCodes.Status409Conflict,
            title: "Das eigene Konto kann man nicht selbst löschen oder herabstufen. Das macht ein anderer Admin."),
    };
}
