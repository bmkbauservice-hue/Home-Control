using HomeControl.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace HomeControl.Api.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(
        DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<DeviceEvent> DeviceEvents => Set<DeviceEvent>();
    public DbSet<BlinkCredential> BlinkCredentials => Set<BlinkCredential>();
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    // Zeitpunkte als Millisekunden seit 1970 (UTC) speichern, wie bei DeviceEvent.Timestamp.
    // Grund: SQLite kann DateTimeOffset nicht vergleichen oder sortieren, Zahlen schon.
    // Bei Feldern, die null sein dürfen (RevokedAtUtc), bleibt null einfach null.
    private static readonly ValueConverter<DateTimeOffset, long> UnixMillisecondsConverter = new(
        value => value.ToUnixTimeMilliseconds(),
        value => DateTimeOffset.FromUnixTimeMilliseconds(value));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Room>(entity =>
        {
            entity.Property(r => r.Name)
                .HasMaxLength(100)
                .IsRequired();

            entity.HasMany(r => r.Devices)
                .WithOne(d => d.Room)
                .HasForeignKey(d => d.RoomId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Device>(entity =>
        {
            entity.Property(d => d.Name)
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(d => d.Type)
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(d => d.LastValue)
                .HasMaxLength(200);

            // Leer (null) bei Geräten ohne Tuya-Verknüpfung. SQLite erlaubt im eindeutigen
            // Index beliebig viele null-Werte, aber jede Tuya-Id nur einmal.
            entity.Property(d => d.TuyaDeviceId)
                .HasMaxLength(64);

            entity.HasIndex(d => d.TuyaDeviceId)
                .IsUnique();

            entity.HasMany(d => d.Events)
                .WithOne(e => e.Device)
                .HasForeignKey(e => e.DeviceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DeviceEvent>(entity =>
        {
            entity.Property(e => e.Action)
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(e => e.Value)
                .HasMaxLength(200);

            entity.Property(e => e.Timestamp)
                .HasConversion(
                    value => value.ToUnixTimeMilliseconds(),
                    value => DateTimeOffset.FromUnixTimeMilliseconds(value));
        });

        modelBuilder.Entity<BlinkCredential>(entity =>
        {
            entity.Property(c => c.HardwareId)
                .HasMaxLength(64)
                .IsRequired();

            // Verschlüsselter Text (Data Protection), deutlich länger als das rohe Token.
            entity.Property(c => c.EncryptedRefreshToken)
                .HasMaxLength(4000)
                .IsRequired();

            // SHA-256 als Hex-Zeichenkette (64 Zeichen).
            entity.Property(c => c.InitialRefreshTokenHash)
                .HasMaxLength(64);
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.Property(u => u.Email)
                .HasMaxLength(256)
                .IsRequired();

            // Jede E-Mail-Adresse nur einmal. Klein geschrieben wird vorher im Code,
            // denn der eindeutige Index in SQLite unterscheidet Groß- und Kleinschreibung.
            entity.HasIndex(u => u.Email)
                .IsUnique();

            entity.Property(u => u.PasswordHash)
                .HasMaxLength(500)
                .IsRequired();

            entity.Property(u => u.Role)
                .HasMaxLength(20)
                .IsRequired();

            entity.Property(u => u.CreatedAtUtc)
                .HasConversion(UnixMillisecondsConverter);

            // Benutzer löschen → seine Refresh-Tokens werden mitgelöscht.
            entity.HasMany(u => u.RefreshTokens)
                .WithOne(t => t.User)
                .HasForeignKey(t => t.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            // SHA-256 als Hex-Zeichenkette (64 Zeichen), eindeutig für die Suche beim Refresh.
            entity.Property(t => t.TokenHash)
                .HasMaxLength(64)
                .IsRequired();

            entity.HasIndex(t => t.TokenHash)
                .IsUnique();

            entity.Property(t => t.CreatedAtUtc)
                .HasConversion(UnixMillisecondsConverter);

            entity.Property(t => t.ExpiresAtUtc)
                .HasConversion(UnixMillisecondsConverter);

            entity.Property(t => t.RevokedAtUtc)
                .HasConversion(UnixMillisecondsConverter);
        });
    }
}
