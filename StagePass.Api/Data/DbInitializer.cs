// Development seed data only. Never call in production.
using Microsoft.EntityFrameworkCore;
using StagePass.Api.Entities;
using StagePass.Api.Enums;

namespace StagePass.Api.Data;

public static class DbInitializer
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StagePassDbContext>();

        if (await db.Venues.AnyAsync())
            return;

        var venueA = new Venue { Name = "Aurora Concert Hall", Address = "12 Lantern Street", City = "Brightmoor" };
        var venueB = new Venue { Name = "Harbor Lights Theatre", Address = "45 Quayside Avenue", City = "Port Elyse" };

        var organizer = new Organizer { Name = "Midnight Sun Productions", Email = "contact@midnightsun.example" };

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

    private static TicketType Ticket(Performance p, string name, decimal price, int capacity) => new()
    {
        Performance = p, Name = name, Price = price, Capacity = capacity, Available = capacity
    };
}
