namespace HomeControl.Api.Auth;

// Der Teil eines Benutzers, den AuthService nach außen gibt: Id, E-Mail und Rolle.
// Die Rolle wird bei Identity über UserManager.GetRolesAsync gelesen, steht also nicht im
// User-Objekt. Passwort-Hash und andere Identity-Felder verlassen den AuthService nie.
public record AuthUser(Guid Id, string Email, string Role);
