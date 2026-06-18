using BeerBot.Models;
using Microsoft.EntityFrameworkCore;

namespace BeerBot.Data;

/// <summary>
/// EF Core database context for BeerBot. Exposes the bot's tables and configures their
/// relationships and indexes.
/// </summary>
public class BeerBotDbContext(DbContextOptions<BeerBotDbContext> options) : DbContext(options)
{
    /// <summary>Registered group members.</summary>
    public DbSet<User> Users => Set<User>();

    /// <summary>Beertime rounds.</summary>
    public DbSet<MeetingRequest> MeetingRequests => Set<MeetingRequest>();

    /// <summary>Per-user responses to a round.</summary>
    public DbSet<Availability> Availabilities => Set<Availability>();

    /// <summary>Individual selected hour blocks.</summary>
    public DbSet<AvailabilitySlot> AvailabilitySlots => Set<AvailabilitySlot>();

    /// <summary>
    /// Configures entity relationships and indexes: a unique Telegram id per user,
    /// cascade deletes from request and user down to availabilities and slots, and a
    /// unique availability per (request, user) pair.
    /// </summary>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>().HasIndex(u => u.TelegramId).IsUnique();

        modelBuilder.Entity<Availability>(b =>
        {
            b.HasOne(a => a.MeetingRequest)
                .WithMany(r => r.Availabilities)
                .HasForeignKey(a => a.RequestId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasOne(a => a.User)
                .WithMany()
                .HasForeignKey(a => a.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasMany(a => a.Slots)
                .WithOne(s => s.Availability)
                .HasForeignKey(s => s.AvailabilityId)
                .OnDelete(DeleteBehavior.Cascade);

            // One availability per user per request.
            b.HasIndex(a => new { a.RequestId, a.UserId }).IsUnique();
        });
    }
}
