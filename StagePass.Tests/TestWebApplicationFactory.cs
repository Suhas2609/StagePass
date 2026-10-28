using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StagePass.Api.Data;

namespace StagePass.Tests;

[CollectionDefinition("DatabaseIntegrationTests", DisableParallelization = true)]
public class DatabaseIntegrationTestsCollection : ICollectionFixture<TestWebApplicationFactory>
{
}

public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    private const string FallbackTestConnectionString =
        "Server=localhost;Database=StagePassTestDb;Trusted_Connection=True;TrustServerCertificate=True;";

    public const string DefaultTestOrganizerApiKey = "test-only-organizer-api-key";

    private static readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;

    public static string ResolveTestConnectionString()
    {
        var envConn = Environment.GetEnvironmentVariable("STAGEPAS_TEST_CONNECTIONSTRING")
                      ?? Environment.GetEnvironmentVariable("STAGEPASS_TEST_CONNECTIONSTRING");
        return !string.IsNullOrWhiteSpace(envConn) ? envConn : FallbackTestConnectionString;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            var testConn = ResolveTestConnectionString();
            var built = config.Build();
            var existingKey = built["OrganizerApiKey"];

            var overrides = new Dictionary<string, string?>
            {
                // Unconditionally pin tests to the isolated test database even if a derived
                // host switches environment to Development (where User Secrets would otherwise load).
                ["ConnectionStrings:DefaultConnection"] = testConn
            };

            if (string.IsNullOrWhiteSpace(existingKey))
            {
                overrides["OrganizerApiKey"] = DefaultTestOrganizerApiKey;
            }

            config.AddInMemoryCollection(overrides);
        });
    }

    public async Task ResetDatabaseAsync()
    {
        await _initLock.WaitAsync();
        try
        {
            using var scope = Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<StagePassDbContext>();

            var dbName = context.Database.GetDbConnection().Database;
            if (!string.Equals(dbName, "StagePassTestDb", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Refusing to run test reset against non-test database '{dbName}'. Expected 'StagePassTestDb'.");
            }

            if (!_initialized)
            {
                await context.Database.MigrateAsync();
                await DbInitializer.SeedAsync(Services);
                _initialized = true;
            }
            else if (!await context.Venues.AnyAsync())
            {
                await DbInitializer.SeedAsync(Services);
            }

            // Order matters — FK constraints
            await context.Database.ExecuteSqlRawAsync("DELETE FROM Bookings");
            await context.Database.ExecuteSqlRawAsync("DELETE FROM ReservationHolds");
            await context.Database.ExecuteSqlRawAsync("DELETE FROM Events WHERE Category = 'Demo'");
            await context.Database.ExecuteSqlRawAsync("DELETE FROM Venues WHERE Name = 'Demo Venue'");
            await context.Database.ExecuteSqlRawAsync("DELETE FROM Organizers WHERE Email = 'demo@stagepass.local'");
            await context.Database.ExecuteSqlRawAsync("UPDATE TicketTypes SET Available = Capacity");
            await context.Database.ExecuteSqlRawAsync("UPDATE Performances SET Status = 'Upcoming'");
        }
        finally
        {
            _initLock.Release();
        }
    }
}

public class StagePassWebApplicationFactory : TestWebApplicationFactory
{
}
