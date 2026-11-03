// Development seed data only. Never call in production.
using Microsoft.EntityFrameworkCore;
using StagePass.Api.Entities;
using StagePass.Api.Enums;
using StagePass.Api.Security;

namespace StagePass.Api.Data;

public static class DbInitializer
{
    public const string DemoOrganizerEmail = "admin@stagepass.local";

    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StagePassDbContext>();

        await SeedDemoOrganizerUserAsync(db);

        if (await db.Venues.AnyAsync())
            return;

        var venueA = new Venue { Name = "Aurora Concert Hall", Address = "12 Lantern Street", City = "Brightmoor" };
        var venueB = new Venue { Name = "Harbor Lights Theatre", Address = "45 Quayside Avenue", City = "Port Elyse" };

        var organizer = new Organizer { Name = "Midnight Sun Productions", Email = DemoOrganizerEmail };

        var concert = new Event
        {
            Organizer = organizer, Venue = venueA,
            Title = "Symphony Under the Stars", Category = "Music",
            Description = "An evening of orchestral classics."
        };
        var play = new Event
        {
            Organizer = organizer, Venue = venueB,
            Title = "The Lighthouse Keeper", Category = "Theatre",
            Description = "A touring stage drama."
        };

        var now = DateTime.UtcNow;
        var perf1 = new Performance { Event = concert, StartDateTime = now.AddDays(1), Status = PerformanceStatus.Upcoming };
        var perf2 = new Performance { Event = concert, StartDateTime = now.AddDays(3), Status = PerformanceStatus.Upcoming };
        var perf3 = new Performance { Event = play, StartDateTime = now.AddDays(7), Status = PerformanceStatus.Upcoming };

        db.Performances.AddRange(perf1, perf2, perf3);
        db.TicketTypes.AddRange(
            Ticket(perf1, "Standard", 25.00m, 100), Ticket(perf1, "Premium", 60.00m, 50),
            Ticket(perf2, "Standard", 25.00m, 100), Ticket(perf2, "Premium", 60.00m, 50),
            Ticket(perf3, "Standard", 30.00m, 80),  Ticket(perf3, "Premium", 75.00m, 50));

        await db.SaveChangesAsync();
    }

    // Seeds the demo organizer login (admin@stagepass.local / StagePass2026! - development/test only) and makes
    // sure the seeded Organizer record carries the same email, since AppUser links to Organizer by email.
    // Idempotent and independent of the Venues guard so databases seeded before authentication existed are upgraded.
    private static async Task SeedDemoOrganizerUserAsync(StagePassDbContext db)
    {
        if (!await db.AppUsers.AnyAsync(u => u.Email == DemoOrganizerEmail))
        {
            var (hash, salt) = PasswordHasher.Hash("StagePass2026!");
            db.AppUsers.Add(new AppUser
            {
                Email = DemoOrganizerEmail,
                Name = "Demo Organizer",
                PasswordHash = hash,
                PasswordSalt = salt,
                IsOrganizer = true,
                CreatedAt = DateTime.UtcNow,
            });
        }

        if (!await db.Organizers.AnyAsync(o => o.Email == DemoOrganizerEmail))
        {
            // Pre-auth seed data used a placeholder email for the single seeded organizer; re-point it.
            var legacy = await db.Organizers.FirstOrDefaultAsync(o => o.Email == "contact@midnightsun.example");
            if (legacy is not null)
                legacy.Email = DemoOrganizerEmail;
        }

        await db.SaveChangesAsync();
    }

    private static TicketType Ticket(Performance p, string name, decimal price, int capacity) => new()
    {
        Performance = p, Name = name, Price = price, Capacity = capacity, Available = capacity
    };
}
