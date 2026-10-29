using HomeControl.Api.Data;
using HomeControl.Api.Models;
using Microsoft.AspNetCore.Identity;

namespace HomeControl.Api.Auth;

// Richtet ASP.NET Core Identity ein. Wird von Program.cs und den Tests benutzt, damit beide
// dieselben Regeln haben.
public static class IdentityRegistration
{
    public static IServiceCollection AddHomeControlIdentity(this IServiceCollection services)
    {
        services
            // AddIdentityCore: nur Benutzerverwaltung (UserManager, PasswordHasher, Validierung),
            // ohne Cookie-Anmeldung und ohne fertige Oberfläche. Die Anmeldung läuft bei uns mit JWT.
            .AddIdentityCore<User>(options =>
            {
                // Jede E-Mail-Adresse nur einmal.
                options.User.RequireUniqueEmail = true;

                // Passwortregeln. Identity prüft sie bei CreateAsync und liefert Fehler als IdentityResult.
                options.Password.RequiredLength = 8;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = false;

                // Sperre nach Fehlversuchen: 5 falsche Passwörter, dann 5 Minuten gesperrt.
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
            })
            // Rollen (Admin, User) in den Identity-Tabellen.
            .AddRoles<IdentityRole<Guid>>()
            // Speichert Benutzer und Rollen über unseren ApplicationDbContext in SQLite.
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddErrorDescriber<GermanIdentityErrorDescriber>();

        return services;
    }
}

// Deutsche Fehlertexte für die häufigsten Identity-Fehler (Standard ist Englisch).
public class GermanIdentityErrorDescriber : IdentityErrorDescriber
{
    public override IdentityError PasswordTooShort(int length) =>
        new() { Code = nameof(PasswordTooShort), Description = $"Das Passwort muss mindestens {length} Zeichen lang sein." };

    public override IdentityError PasswordRequiresDigit() =>
        new() { Code = nameof(PasswordRequiresDigit), Description = "Das Passwort braucht mindestens eine Ziffer." };

    public override IdentityError PasswordRequiresLower() =>
        new() { Code = nameof(PasswordRequiresLower), Description = "Das Passwort braucht mindestens einen Kleinbuchstaben." };

    public override IdentityError PasswordRequiresUpper() =>
        new() { Code = nameof(PasswordRequiresUpper), Description = "Das Passwort braucht mindestens einen Großbuchstaben." };

    public override IdentityError PasswordRequiresNonAlphanumeric() =>
        new() { Code = nameof(PasswordRequiresNonAlphanumeric), Description = "Das Passwort braucht mindestens ein Sonderzeichen." };

    public override IdentityError DuplicateEmail(string email) =>
        new() { Code = nameof(DuplicateEmail), Description = "Diese E-Mail-Adresse ist schon registriert." };

    public override IdentityError DuplicateUserName(string userName) =>
        new() { Code = nameof(DuplicateUserName), Description = "Diese E-Mail-Adresse ist schon registriert." };

    public override IdentityError InvalidEmail(string? email) =>
        new() { Code = nameof(InvalidEmail), Description = "Bitte eine gültige E-Mail-Adresse eingeben." };
}
