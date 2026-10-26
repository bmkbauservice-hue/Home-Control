using HomeControl.Api.Models;
using Microsoft.EntityFrameworkCore;

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
    }
}
