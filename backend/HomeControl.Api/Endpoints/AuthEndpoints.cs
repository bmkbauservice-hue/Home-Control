using System.Security.Claims;
using HomeControl.Api.Auth;
using HomeControl.Api.Contracts;
using HomeControl.Api.Models;
using Microsoft.IdentityModel.JsonWebTokens;

namespace HomeControl.Api.Endpoints;

public static class AuthEndpoints
{
    // Name des Cookies, in dem das Refresh-Token liegt.
    public const string RefreshCookieName = "hc_refresh";

    // Der Browser schickt das Cookie nur an Adressen unter /api/auth, also nicht bei jedem
    // Abruf von Räumen oder Geräten mit. Gebraucht wird es nur bei refresh und logout.
    private const string RefreshCookiePath = "/api/auth";

    private const int MaxEmailLength = 256;
    private const int MinPasswordLength = 8;
    // Obergrenze, damit niemand den Server mit riesigen Passwörtern beschäftigt (Hashing ist
    // absichtlich langsam).
    private const int MaxPasswordLength = 128;

    // Registriert alle Endpunkte unter /api/auth.
    public static void MapAuthEndpoints(this WebApplication app)
    {
        var auth = app.MapGroup("/api/auth");

        auth.MapPost("/register", Register);
        auth.MapPost("/login", Login);
        auth.MapPost("/refresh", Refresh);
        auth.MapPost("/logout", Logout);
        // Nur mit gültigem Access Token (Authorization: Bearer ...), sonst 401.
        auth.MapGet("/profile", GetProfile).RequireAuthorization();
    }

    // POST /api/auth/register – Benutzer anlegen (erster Benutzer: jeder, danach nur Admin)
    private static async Task<IResult> Register(
        RegisterRequest request,
        ClaimsPrincipal caller,
        AuthService authService,
        CancellationToken cancellationToken)
    {
        var errors = ValidateEmailAndPassword(request.Email, request.Password, checkPasswordRules: true);
        if (request.Role is not null && request.Role != Roles.Admin && request.Role != Roles.User)
        {
            errors["role"] = ["Rolle muss \"Admin\" oder \"User\" sein."];
        }
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var result = await authService.RegisterAsync(
            request.Email,
            request.Password,
            request.Role,
            callerIsAdmin: caller.IsInRole(Roles.Admin),
            cancellationToken);

        return result.Status switch
        {
            RegisterStatus.Created => Results.Json(ToUserResponse(result.User!), statusCode: StatusCodes.Status201Created),
            RegisterStatus.EmailTaken => Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["email"] = ["Diese E-Mail-Adresse ist schon registriert."]
            }),
            // 401: gar nicht angemeldet. 403: angemeldet, aber kein Admin.
            _ when caller.Identity?.IsAuthenticated == true => Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Nur ein Admin darf neue Benutzer anlegen."),
            _ => Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Bitte zuerst als Admin anmelden."),
        };
    }

    // POST /api/auth/login – E-Mail und Passwort prüfen, Tokens ausgeben
    private static async Task<IResult> Login(
        LoginRequest request,
        AuthService authService,
        HttpContext http,
        IWebHostEnvironment environment,
        CancellationToken cancellationToken)
    {
        // Beim Login keine Passwortregeln prüfen: Sonst verrät die Antwort etwas über die Regeln
        // statt nur "falsch". Leer oder viel zu lang ist trotzdem ein Eingabefehler.
        var errors = ValidateEmailAndPassword(request.Email, request.Password, checkPasswordRules: false);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var tokens = await authService.LoginAsync(request.Email, request.Password, cancellationToken);
        if (tokens is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "E-Mail oder Passwort ist falsch.");
        }

        SetRefreshCookie(http, environment, tokens);
        return Results.Ok(ToLoginResponse(tokens));
    }

    // POST /api/auth/refresh – mit dem Refresh-Token aus dem Cookie ein neues Access Token holen.
    // Kein Body: Das Token kommt aus dem HTTP-only Cookie, das JavaScript gar nicht lesen kann.
    private static async Task<IResult> Refresh(
        AuthService authService,
        HttpContext http,
        IWebHostEnvironment environment,
        CancellationToken cancellationToken)
    {
        var refreshToken = http.Request.Cookies[RefreshCookieName];
        var tokens = await authService.RefreshAsync(refreshToken, cancellationToken);
        if (tokens is null)
        {
            DeleteRefreshCookie(http, environment);
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Sitzung abgelaufen. Bitte neu anmelden.");
        }

        SetRefreshCookie(http, environment, tokens);
        return Results.Ok(ToLoginResponse(tokens));
    }

    // POST /api/auth/logout – Refresh-Token widerrufen und Cookie löschen. Antwortet immer mit
    // 204, auch ohne Cookie: Abmelden soll nie an einem Fehler scheitern.
    private static async Task<IResult> Logout(
        AuthService authService,
        HttpContext http,
        IWebHostEnvironment environment,
        CancellationToken cancellationToken)
    {
        await authService.LogoutAsync(http.Request.Cookies[RefreshCookieName], cancellationToken);
        DeleteRefreshCookie(http, environment);
        return Results.NoContent();
    }

    // GET /api/auth/profile – wer bin ich? Die Id steht im Claim "sub" des Access Tokens.
    private static async Task<IResult> GetProfile(
        ClaimsPrincipal user,
        AuthService authService,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(user.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var userId))
        {
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Ungültiges Token.");
        }

        // Benutzer neu aus der Datenbank lesen: Er könnte inzwischen gelöscht worden sein.
        var found = await authService.FindUserAsync(userId, cancellationToken);
        return found is null
            ? Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Benutzer existiert nicht mehr.")
            : Results.Ok(ToUserResponse(found));
    }

    // Sicherheitsoptionen des Refresh-Cookies:
    // - HttpOnly: JavaScript kann das Cookie nicht lesen. Ein eingeschleustes Skript (XSS)
    //   kommt so nicht an das langlebige Token.
    // - Secure: nur über HTTPS senden. Lokal läuft Home Control über http://localhost,
    //   deshalb nur außerhalb von Development. Im echten Betrieb ist HTTPS Pflicht.
    // - SameSite=Strict: Der Browser schickt das Cookie nicht mit, wenn die Anfrage von einer
    //   fremden Seite ausgelöst wird. Das schützt refresh und logout vor CSRF.
    // - Path: nur an /api/auth senden, siehe oben.
    private static CookieOptions CreateRefreshCookieOptions(IWebHostEnvironment environment) => new()
    {
        HttpOnly = true,
        Secure = !environment.IsDevelopment(),
        SameSite = SameSiteMode.Strict,
        Path = RefreshCookiePath,
    };

    private static void SetRefreshCookie(HttpContext http, IWebHostEnvironment environment, AuthTokens tokens)
    {
        var cookieOptions = CreateRefreshCookieOptions(environment);
        cookieOptions.Expires = tokens.RefreshTokenExpiresAtUtc;
        http.Response.Cookies.Append(RefreshCookieName, tokens.RefreshToken, cookieOptions);
    }

    // Zum Löschen müssen Path und die übrigen Optionen gleich sein wie beim Setzen.
    private static void DeleteRefreshCookie(HttpContext http, IWebHostEnvironment environment) =>
        http.Response.Cookies.Delete(RefreshCookieName, CreateRefreshCookieOptions(environment));

    private static Dictionary<string, string[]> ValidateEmailAndPassword(
        string? email,
        string? password,
        bool checkPasswordRules)
    {
        var errors = new Dictionary<string, string[]>();

        var trimmedEmail = email?.Trim() ?? string.Empty;
        var atIndex = trimmedEmail.IndexOf('@');
        if (trimmedEmail.Length == 0)
        {
            errors["email"] = ["Bitte eine E-Mail-Adresse eingeben."];
        }
        else if (trimmedEmail.Length > MaxEmailLength || atIndex <= 0 || atIndex == trimmedEmail.Length - 1)
        {
            errors["email"] = ["Bitte eine gültige E-Mail-Adresse eingeben."];
        }

        if (string.IsNullOrEmpty(password))
        {
            errors["password"] = ["Bitte ein Passwort eingeben."];
        }
        else if (password.Length > MaxPasswordLength)
        {
            errors["password"] = [$"Das Passwort darf höchstens {MaxPasswordLength} Zeichen lang sein."];
        }
        else if (checkPasswordRules && password.Length < MinPasswordLength)
        {
            errors["password"] = [$"Das Passwort muss mindestens {MinPasswordLength} Zeichen lang sein."];
        }

        return errors;
    }

    private static UserResponse ToUserResponse(User user) => new(user.Id, user.Email, user.Role);

    private static LoginResponse ToLoginResponse(AuthTokens tokens) =>
        new(tokens.AccessToken.Token, tokens.AccessToken.ExpiresAtUtc, ToUserResponse(tokens.User));
}
