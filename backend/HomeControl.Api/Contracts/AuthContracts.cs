namespace HomeControl.Api.Contracts;

// Role ist optional und wird nur beachtet, wenn ein Admin einen Benutzer anlegt
// ("Admin" oder "User", Standard "User"). Der allererste Benutzer wird immer Admin.
public record RegisterRequest(string Email, string Password, string? Role);

public record LoginRequest(string Email, string Password);

// Was die API über einen Benutzer herausgibt. Nie den Passwort-Hash.
public record UserResponse(Guid Id, string Email, string Role);

// Antwort auf Login und Refresh. Das Refresh-Token steht bewusst NICHT im JSON, sondern nur im
// HTTP-only Cookie: JavaScript im Browser kommt so nicht daran (Schutz bei XSS).
public record LoginResponse(string AccessToken, DateTimeOffset AccessTokenExpiresAtUtc, UserResponse User);
