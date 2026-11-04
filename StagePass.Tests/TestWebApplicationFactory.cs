using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StagePass.Api.Data;
using StagePass.Api.Entities;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace StagePass.Tests;

[CollectionDefinition("DatabaseIntegrationTests", DisableParallelization = true)]
public class DatabaseIntegrationTestsCollection : ICollectionFixture<TestWebApplicationFactory>
{
}

[CollectionDefinition("Integration", DisableParallelization = true)]
public class IntegrationCollection : ICollectionFixture<StagePassTestFixture>
{
}

public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    private const string FallbackTestConnectionString =
        "Server=localhost;Database=StagePassTestDb;Trusted_Connection=True;TrustServerCertificate=True;";

    public const string OrganizerEmail = "admin@stagepass.local";
    public const string OrganizerPassword = "StagePass2026!";

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

            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Unconditionally pin tests to the isolated test database even if a derived
                // host switches environment to Development (where User Secrets would otherwise load).
                ["ConnectionStrings:DefaultConnection"] = testConn
            });
        });
    }

    /// <summary>Logs in as the seeded demo organizer and returns the session token.</summary>
    public async Task<string> LoginAsOrganizerAsync()
    {
        using var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login",
            new { email = OrganizerEmail, password = OrganizerPassword });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("token").GetString()!;
    }

    /// <summary>Registers a fresh user with a unique email and returns (token, email). Cleaned up by ResetDatabaseAsync.</summary>
    public async Task<(string Token, string Email)> RegisterUserAsync(bool isOrganizer = false, string password = "Passw0rd!123")
    {
        var email = $"user-{Guid.NewGuid():N}@example.com";
        using var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register",
            new { email, password, name = "Test User", isOrganizer });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (body.GetProperty("token").GetString()!, email);
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
                _initialized = true;
            }

            // Idempotent: seeds the organizer login and (when the DB is empty) the sample data.
            await DbInitializer.SeedAsync(Services);

            // Order matters — FK constraints
            await context.Database.ExecuteSqlRawAsync("DELETE FROM AppSessions");
            await context.Database.ExecuteSqlRawAsync($"DELETE FROM AppUsers WHERE Email <> '{OrganizerEmail}'");
            await context.Database.ExecuteSqlRawAsync("DELETE FROM Bookings");
            await context.Database.ExecuteSqlRawAsync("DELETE FROM ReservationHolds");
            await context.Database.ExecuteSqlRawAsync($"DELETE FROM TicketTypes WHERE PerformanceId IN (SELECT PerformanceId FROM Performances WHERE EventId IN (SELECT EventId FROM Events WHERE OrganizerId IN (SELECT OrganizerId FROM Organizers WHERE Email <> '{OrganizerEmail}')))");
            await context.Database.ExecuteSqlRawAsync($"DELETE FROM Performances WHERE EventId IN (SELECT EventId FROM Events WHERE OrganizerId IN (SELECT OrganizerId FROM Organizers WHERE Email <> '{OrganizerEmail}'))");
            await context.Database.ExecuteSqlRawAsync($"DELETE FROM Events WHERE OrganizerId IN (SELECT OrganizerId FROM Organizers WHERE Email <> '{OrganizerEmail}')");
            await context.Database.ExecuteSqlRawAsync("DELETE FROM Events WHERE Category = 'Demo'");
            await context.Database.ExecuteSqlRawAsync("DELETE FROM Venues WHERE Name = 'Demo Venue'");
            await context.Database.ExecuteSqlRawAsync($"DELETE FROM Organizers WHERE Email NOT IN ('{OrganizerEmail}', 'demo@stagepass.local')");
            await context.Database.ExecuteSqlRawAsync("DELETE FROM Organizers WHERE Email = 'demo@stagepass.local'");
            await context.Database.ExecuteSqlRawAsync("UPDATE TicketTypes SET Available = Capacity");
            await context.Database.ExecuteSqlRawAsync("UPDATE Performances SET Status = 'Upcoming'");
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task<TicketType> GetSeedTicketTypeAsync()
    {
        using var scope = Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<StagePassDbContext>();
        return await ctx.TicketTypes.AsNoTracking().FirstAsync();
    }
}

public class StagePassWebApplicationFactory : TestWebApplicationFactory
{
}

public class StagePassTestFixture : TestWebApplicationFactory
{
}
