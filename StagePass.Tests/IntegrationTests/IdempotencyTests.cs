using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StagePass.Api.Data;
using StagePass.Api.DTOs.Reservations;

namespace StagePass.Tests.IntegrationTests;

[Collection("DatabaseIntegrationTests")]
public class IdempotencyTests : IAsyncLifetime
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public IdempotencyTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await _factory.ResetDatabaseAsync();
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        return Task.CompletedTask;
    }

    private async Task<(int TicketTypeId, int InitialAvailable)> GetFirstTicketTypeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StagePassDbContext>();
        var tt = await db.TicketTypes.AsNoTracking().OrderBy(t => t.TicketTypeId).FirstAsync();
        return (tt.TicketTypeId, tt.Available);
    }

    [Fact]
    public async Task SameKey_SequentialRetry_Returns200WithSameHoldId()
    {
        var (ticketTypeId, initialAvailable) = await GetFirstTicketTypeAsync();
        var idempotencyKey = Guid.NewGuid();

        var request = new CreateReservationRequest
        {
            TicketTypeId = ticketTypeId,
            Quantity = 1,
            IdempotencyKey = idempotencyKey,
            CustomerIdentifier = "retry-user@example.com"
        };

        var firstResponse = await _client.PostAsJsonAsync("/api/reservations", request);
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        var firstHold = await firstResponse.Content.ReadFromJsonAsync<ReservationResponse>();
        Assert.NotNull(firstHold);

        var secondResponse = await _client.PostAsJsonAsync("/api/reservations", request);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        var secondHold = await secondResponse.Content.ReadFromJsonAsync<ReservationResponse>();
        Assert.NotNull(secondHold);
        Assert.Equal(firstHold!.HoldId, secondHold!.HoldId);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StagePassDbContext>();
        var updatedTicketType = await db.TicketTypes
            .AsNoTracking()
            .FirstAsync(tt => tt.TicketTypeId == ticketTypeId);
        Assert.Equal(initialAvailable - 1, updatedTicketType.Available);
    }

    [Fact]
    public async Task SameKey_DifferentParams_Returns409IdempotencyKeyReused()
    {
        var (ticketTypeId, _) = await GetFirstTicketTypeAsync();
        var key = Guid.NewGuid();

        var firstResponse = await _client.PostAsJsonAsync("/api/reservations", new CreateReservationRequest
        {
            TicketTypeId = ticketTypeId,
            Quantity = 1,
            IdempotencyKey = key,
            CustomerIdentifier = "reuse-user@example.com"
        });
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);

        var secondResponse = await _client.PostAsJsonAsync("/api/reservations", new CreateReservationRequest
        {
            TicketTypeId = ticketTypeId,
            Quantity = 3,
            IdempotencyKey = key,
            CustomerIdentifier = "reuse-user@example.com"
        });

        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
        var body = await secondResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("IdempotencyKeyReused", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task SameKey_Concurrent_CreatesExactlyOneHold()
    {
        var (seedTicketTypeId, initialAvailable) = await GetFirstTicketTypeAsync();
        var sharedKey = Guid.NewGuid();

        var tasks = Enumerable.Range(0, 50)
            .Select(_ => _client.PostAsJsonAsync("/api/reservations", new
            {
                ticketTypeId = seedTicketTypeId,
                quantity = 1,
                idempotencyKey = sharedKey,
                customerIdentifier = "test@example.com"
            }))
            .ToList();

        var responses = await Task.WhenAll(tasks);

        Assert.All(responses, r =>
            Assert.True(r.StatusCode == HttpStatusCode.OK || r.StatusCode == HttpStatusCode.Created,
                $"Expected 200 or 201, got {(int)r.StatusCode}"));

        var holdIds = new List<int>();
        foreach (var r in responses)
        {
            var body = await r.Content.ReadFromJsonAsync<ReservationResponse>();
            Assert.NotNull(body);
            holdIds.Add(body!.HoldId);
        }

        Assert.Single(holdIds.Distinct());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StagePassDbContext>();
        var updatedTicketType = await db.TicketTypes
            .AsNoTracking()
            .FirstAsync(tt => tt.TicketTypeId == seedTicketTypeId);
        Assert.Equal(initialAvailable - 1, updatedTicketType.Available);
    }
}
