using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StagePass.Api.Data;
using StagePass.Api.DTOs.Reservations;
using Xunit;

namespace StagePass.Tests.IntegrationTests;

[Collection("DatabaseIntegrationTests")]
public class DemoAndSecurityTests
{
    private readonly TestWebApplicationFactory _factory;

    public DemoAndSecurityTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private static void AssertIsolatedTestDatabase(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StagePassDbContext>();
        var dbName = db.Database.GetDbConnection().Database;
        Assert.Equal("StagePassTestDb", dbName, ignoreCase: true);
    }

    private static async Task<int> FirstTicketTypeIdAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StagePassDbContext>();
        return (await db.TicketTypes.AsNoTracking().OrderBy(tt => tt.TicketTypeId).FirstAsync()).TicketTypeId;
    }

    [Theory]
    [InlineData("Testing")]
    [InlineData("Production")]
    public async Task DemoEndpoints_NonDevelopmentEnvironment_Return404NotFound(string environmentName)
    {
        await _factory.ResetDatabaseAsync();

        await using var customFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environmentName);
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = TestWebApplicationFactory.ResolveTestConnectionString(),
                    ["RateLimiting:BookingEnabled"] = "false"
                });
            });
        });

        using var client = customFactory.CreateClient();
        AssertIsolatedTestDatabase(customFactory.Services);

        var setupResp = await client.PostAsync("/api/demo/setup-scenario?tickets=50&scenarioName=ProdCheck", null);
        Assert.Equal(HttpStatusCode.NotFound, setupResp.StatusCode);

        var snapshotResp = await client.GetAsync("/api/demo/snapshot/1");
        Assert.Equal(HttpStatusCode.NotFound, snapshotResp.StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    [InlineData(10001)]
    public async Task DemoSetupScenario_InvalidTicketCounts_InDevelopment_Returns400AndCreatesNoEntities(int invalidTickets)
    {
        await _factory.ResetDatabaseAsync();

        await using var devFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = TestWebApplicationFactory.ResolveTestConnectionString(),
                    ["RateLimiting:BookingEnabled"] = "false"
                });
            });
        });

        using var client = devFactory.CreateClient();
        AssertIsolatedTestDatabase(devFactory.Services);

        int eventCountBefore;
        using (var scope = devFactory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StagePassDbContext>();
            eventCountBefore = await db.Events.CountAsync();
        }

        var response = await client.PostAsync(
            $"/api/demo/setup-scenario?tickets={invalidTickets}&scenarioName=BoundsTest",
            null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("InvalidTicketCount", body.GetProperty("error").GetString());

        using (var scope = devFactory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StagePassDbContext>();
            Assert.Equal(eventCountBefore, await db.Events.CountAsync());
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(101)]
    public async Task DemoSetupScenario_InvalidScenarioName_InDevelopment_Returns400AndCreatesNoEntities(object scenarioInput)
    {
        await _factory.ResetDatabaseAsync();

        var scenarioName = scenarioInput is int len ? new string('X', len) : (string)scenarioInput;

        await using var devFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = TestWebApplicationFactory.ResolveTestConnectionString(),
                    ["RateLimiting:BookingEnabled"] = "false"
                });
            });
        });

        using var client = devFactory.CreateClient();
        AssertIsolatedTestDatabase(devFactory.Services);

        int eventCountBefore;
        using (var scope = devFactory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StagePassDbContext>();
            eventCountBefore = await db.Events.CountAsync();
        }

        var response = await client.PostAsync(
            $"/api/demo/setup-scenario?tickets=50&scenarioName={Uri.EscapeDataString(scenarioName)}",
            null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("InvalidScenarioName", body.GetProperty("error").GetString());

        using (var scope = devFactory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StagePassDbContext>();
            Assert.Equal(eventCountBefore, await db.Events.CountAsync());
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task DemoSnapshot_NonPositiveTicketTypeId_InDevelopment_Returns400(int invalidTicketTypeId)
    {
        await _factory.ResetDatabaseAsync();

        await using var devFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = TestWebApplicationFactory.ResolveTestConnectionString(),
                    ["RateLimiting:BookingEnabled"] = "false"
                });
            });
        });

        using var client = devFactory.CreateClient();
        AssertIsolatedTestDatabase(devFactory.Services);

        var response = await client.GetAsync($"/api/demo/snapshot/{invalidTicketTypeId}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("InvalidTicketTypeId", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task DemoSetupAndSnapshot_ValidInput_InDevelopment_CreatesScenarioAndVerifiesDatabaseEffects()
    {
        await _factory.ResetDatabaseAsync();

        await using var devFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = TestWebApplicationFactory.ResolveTestConnectionString(),
                    ["RateLimiting:BookingEnabled"] = "false"
                });
            });
        });

        using var client = devFactory.CreateClient();
        AssertIsolatedTestDatabase(devFactory.Services);

        int createdPerformanceId = 0;
        int createdTicketTypeId = 0;

        try
        {
            var setupResp = await client.PostAsync(
                "/api/demo/setup-scenario?tickets=25&scenarioName=" + Uri.EscapeDataString("  Verified Demo Scenario  "),
                null);
            Assert.Equal(HttpStatusCode.OK, setupResp.StatusCode);

            var setupBody = await setupResp.Content.ReadFromJsonAsync<JsonElement>();
            createdTicketTypeId = setupBody.GetProperty("ticketTypeId").GetInt32();
            createdPerformanceId = setupBody.GetProperty("performanceId").GetInt32();
            Assert.Equal(25, setupBody.GetProperty("capacity").GetInt32());
            Assert.Equal("Verified Demo Scenario", setupBody.GetProperty("scenarioName").GetString());
            Assert.True(createdTicketTypeId > 0);
            Assert.True(createdPerformanceId > 0);

            // Place 1 hold for 3 tickets against the new demo TicketType to verify live snapshot counts
            var holdResp = await client.PostAsJsonAsync("/api/reservations", new CreateReservationRequest
            {
                TicketTypeId = createdTicketTypeId,
                Quantity = 3,
                IdempotencyKey = Guid.NewGuid(),
                CustomerIdentifier = "demo-snapshot-test@stagepass.local"
            });
            Assert.Equal(HttpStatusCode.Created, holdResp.StatusCode);

            var snapResp = await client.GetAsync($"/api/demo/snapshot/{createdTicketTypeId}");
            Assert.Equal(HttpStatusCode.OK, snapResp.StatusCode);

            var snapBody = await snapResp.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(25, snapBody.GetProperty("capacity").GetInt32());
            Assert.Equal(22, snapBody.GetProperty("available").GetInt32());
            Assert.Equal(3, snapBody.GetProperty("pendingHeld").GetInt32());
            Assert.Equal(0, snapBody.GetProperty("confirmed").GetInt32());
            Assert.True(snapBody.GetProperty("invariantHolds").GetBoolean());
        }
        finally
        {
            using var scope = devFactory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<StagePassDbContext>();
            Assert.Equal("StagePassTestDb", db.Database.GetDbConnection().Database, ignoreCase: true);

            if (createdTicketTypeId > 0)
            {
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"DELETE FROM ReservationHolds WHERE TicketTypeId = {createdTicketTypeId}");
            }

            if (createdPerformanceId > 0)
            {
                var perf = await db.Performances.FindAsync(createdPerformanceId);
                if (perf != null)
                {
                    var evt = await db.Events.FindAsync(perf.EventId);
                    if (evt != null)
                    {
                        db.Events.Remove(evt);
                        await db.SaveChangesAsync();
                    }
                }
            }

            var demoVenue = await db.Venues.FirstOrDefaultAsync(v => v.Name == "Demo Venue");
            if (demoVenue != null)
            {
                db.Venues.Remove(demoVenue);
            }

            var demoOrg = await db.Organizers.FirstOrDefaultAsync(o => o.Email == "demo@stagepass.local");
            if (demoOrg != null)
            {
                db.Organizers.Remove(demoOrg);
            }

            await db.SaveChangesAsync();
        }
    }

    [Theory]
    [InlineData("True")]
    [InlineData("true")]
    public async Task RateLimiting_WhenBookingEnabledIsTrue_Rejects11thReservationWith429(string configValue)
    {
        await _factory.ResetDatabaseAsync();

        await using var rateLimitedFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = TestWebApplicationFactory.ResolveTestConnectionString(),
                    ["RateLimiting:BookingEnabled"] = configValue
                });
            });
        });

        using var client = rateLimitedFactory.CreateClient();
        AssertIsolatedTestDatabase(rateLimitedFactory.Services);
        var ticketTypeId = await FirstTicketTypeIdAsync(rateLimitedFactory.Services);

        for (var i = 0; i < 10; i++)
        {
            var resp = await client.PostAsJsonAsync("/api/reservations", new CreateReservationRequest
            {
                TicketTypeId = ticketTypeId,
                Quantity = 1,
                IdempotencyKey = Guid.NewGuid(),
                CustomerIdentifier = $"rl-enabled-{i}@example.com"
            });
            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        }

        var eleventhResp = await client.PostAsJsonAsync("/api/reservations", new CreateReservationRequest
        {
            TicketTypeId = ticketTypeId,
            Quantity = 1,
            IdempotencyKey = Guid.NewGuid(),
            CustomerIdentifier = "rl-enabled-11@example.com"
        });
        Assert.Equal(HttpStatusCode.TooManyRequests, eleventhResp.StatusCode);
    }

    [Theory]
    [InlineData("False")]
    [InlineData("false")]
    public async Task RateLimiting_WhenBookingEnabledIsFalse_AllowsMoreThan10Reservations(string configValue)
    {
        await _factory.ResetDatabaseAsync();

        await using var unlimitedFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = TestWebApplicationFactory.ResolveTestConnectionString(),
                    ["RateLimiting:BookingEnabled"] = configValue
                });
            });
        });

        using var client = unlimitedFactory.CreateClient();
        AssertIsolatedTestDatabase(unlimitedFactory.Services);
        var ticketTypeId = await FirstTicketTypeIdAsync(unlimitedFactory.Services);

        for (var i = 0; i < 12; i++)
        {
            var resp = await client.PostAsJsonAsync("/api/reservations", new CreateReservationRequest
            {
                TicketTypeId = ticketTypeId,
                Quantity = 1,
                IdempotencyKey = Guid.NewGuid(),
                CustomerIdentifier = $"rl-disabled-{i}@example.com"
            });
            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        }
    }

    [Fact]
    public async Task OrganizerEndpoints_WithoutAuthorizationHeader_Return401AuthRequired()
    {
        await _factory.ResetDatabaseAsync();
        using var client = _factory.CreateClient();

        var resp = await client.GetAsync("/api/organizer/performances/1/bookings");

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("AuthRequired", body.GetProperty("error").GetString());
    }

    [Theory]
    [InlineData("Bearer 00000000-0000-0000-0000-000000000001")] // well-formed but unknown session
    [InlineData("Bearer not-a-guid")]
    [InlineData("Bearer ")]
    [InlineData("Basic YWRtaW46cGFzcw==")]
    [InlineData("stagepass-demo-2026")]                           // legacy X-Api-Key style secret is not accepted
    public async Task OrganizerEndpoints_WithInvalidAuthorization_Return401(string authorizationHeader)
    {
        await _factory.ResetDatabaseAsync();
        using var client = _factory.CreateClient();

        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/organizer/performances/1/bookings");
        req.Headers.TryAddWithoutValidation("Authorization", authorizationHeader);

        var resp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task OrganizerEndpoints_LegacyApiKeyHeader_IsIgnored_Returns401()
    {
        await _factory.ResetDatabaseAsync();
        using var client = _factory.CreateClient();

        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/organizer/performances/1/bookings");
        req.Headers.Add("X-Api-Key", "anything");

        var resp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task OrganizerEndpoints_WithNonOrganizerSession_Return403OrganizerRequired()
    {
        await _factory.ResetDatabaseAsync();
        var (token, _) = await _factory.RegisterUserAsync(isOrganizer: false);
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var resp = await client.GetAsync("/api/organizer/performances/1/bookings");

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("OrganizerRequired", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task OrganizerEndpoints_WithExpiredSession_Return401()
    {
        await _factory.ResetDatabaseAsync();
        var token = await _factory.LoginAsOrganizerAsync();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StagePassDbContext>();
            var sessionId = Guid.Parse(token);
            await db.AppSessions.Where(s => s.SessionId == sessionId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiresAt, DateTime.UtcNow.AddMinutes(-1)));
        }

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var resp = await client.GetAsync("/api/organizer/performances/1/bookings");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task OrganizerCreateEvent_IgnoresBodyOrganizerId_UsesAuthenticatedOrganizer()
    {
        await _factory.ResetDatabaseAsync();
        var token = await _factory.LoginAsOrganizerAsync();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        int venueId, organizerId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StagePassDbContext>();
            venueId = (await db.Venues.AsNoTracking().FirstAsync()).VenueId;
            organizerId = (await db.Organizers.AsNoTracking().SingleAsync(o => o.Email == TestWebApplicationFactory.OrganizerEmail)).OrganizerId;
        }

        var resp = await client.PostAsJsonAsync("/api/organizer/events",
            new { title = "Ownership Test", category = "Test", venueId, organizerId = 999999 });
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var eventId = (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("eventId").GetInt32();

        try
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<StagePassDbContext>();
            Assert.Equal(organizerId, (await db.Events.AsNoTracking().SingleAsync(e => e.EventId == eventId)).OrganizerId);
        }
        finally
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<StagePassDbContext>();
            await db.Events.Where(e => e.EventId == eventId).ExecuteDeleteAsync();
        }
    }
}