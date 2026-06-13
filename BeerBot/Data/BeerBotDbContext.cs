using BeerBot.Models;
using Microsoft.EntityFrameworkCore;

namespace BeerBot.Data;

public class BeerBotDbContext(DbContextOptions<BeerBotDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<MeetingRequest> MeetingRequests => Set<MeetingRequest>();
    public DbSet<Availability> Availabilities => Set<Availability>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>().HasIndex(u => u.TelegramId).IsUnique();

        modelBuilder.Entity<Availability>().Property(a => a.ParsedSlotsJson).HasColumnType("jsonb");
    }
}
