using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StagePass.Api.Data;

namespace StagePass.Tests.UnitTests;

[Collection("DatabaseIntegrationTests")]
public class DatabaseSeedVerificationTests
{
    private readonly TestWebApplicationFactory _factory;

    public DatabaseSeedVerificationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task SeedAsync_WhenDatabaseExists_SeedsExpectedEntitiesOnce()
    {
        await _factory.ResetDatabaseAsync();

        // Run seed
        await DbInitializer.SeedAsync(_factory.Services);

        // Verify entities in StagePassTestDb
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StagePassDbContext>();

            var venueCount = await db.Venues.CountAsync();
            var organizerCount = await db.Organizers.CountAsync();
            var eventCount = await db.Events.CountAsync();
            var performanceCount = await db.Performances.CountAsync();
            var ticketTypeCount = await db.TicketTypes.CountAsync();

            Assert.Equal(2, venueCount);
            Assert.Equal(1, organizerCount);
            Assert.Equal(2, eventCount);
            Assert.Equal(3, performanceCount);
            Assert.Equal(6, ticketTypeCount);
        }

        // Verify idempotency guard (calling SeedAsync again does not duplicate data)
        await DbInitializer.SeedAsync(_factory.Services);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StagePassDbContext>();
            Assert.Equal(2, await db.Venues.CountAsync());
            Assert.Equal(1, await db.Organizers.CountAsync());
            Assert.Equal(2, await db.Events.CountAsync());
            Assert.Equal(3, await db.Performances.CountAsync());
            Assert.Equal(6, await db.TicketTypes.CountAsync());
        }
    }
}
