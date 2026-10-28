using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StagePass.Api.BackgroundServices;
using StagePass.Api.Data;
using StagePass.Api.DTOs.Bookings;
using StagePass.Api.DTOs.Reservations;
using StagePass.Api.Enums;

namespace StagePass.Tests.IntegrationTests;

[Collection("DatabaseIntegrationTests")]
public class ReservationLifecycleTests : IAsyncLifetime
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public ReservationLifecycleTests(TestWebApplicationFactory factory)
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
    public async Task CreateHold_SufficientInventory_Returns201_AndDecrementsAvailable()
    {
        var (ticketTypeId, initialAvailable) = await GetFirstTicketTypeAsync();

        var response = await _client.PostAsJsonAsync("/api/reservations", new CreateReservationRequest
        {
            TicketTypeId = ticketTypeId,
            Quantity = 1,
            IdempotencyKey = Guid.NewGuid(),
            CustomerIdentifier = "alice@example.com"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ReservationResponse>();
        Assert.NotNull(body);
        Assert.True(body!.HoldId > 0);
        Assert.Equal("Pending", body.Status);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StagePassDbContext>();
        var updated = await db.TicketTypes.AsNoTracking().FirstAsync(t => t.TicketTypeId == ticketTypeId);
        Assert.Equal(initialAvailable - 1, updated.Available);
    }

    [Fact]
    public async Task CreateHold_InsufficientInventory_Returns409()
    {
        var (ticketTypeId, _) = await GetFirstTicketTypeAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StagePassDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE TicketTypes SET Available = 0 WHERE TicketTypeId = {0}", ticketTypeId);
        }

        var response = await _client.PostAsJsonAsync("/api/reservations", new CreateReservationRequest
        {
            TicketTypeId = ticketTypeId,
            Quantity = 1,
            IdempotencyKey = Guid.NewGuid(),
            CustomerIdentifier = "bob@example.com"
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("InsufficientInventory", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task CreateHold_InvalidQuantity_Returns400()
    {
        var (ticketTypeId, _) = await GetFirstTicketTypeAsync();

        var response = await _client.PostAsJsonAsync("/api/reservations", new CreateReservationRequest
        {
            TicketTypeId = ticketTypeId,
            Quantity = 0,
            IdempotencyKey = Guid.NewGuid(),
            CustomerIdentifier = "invalid@example.com"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ConfirmHold_ValidPending_Returns200_WithBookingReference()
    {
        var (ticketTypeId, _) = await GetFirstTicketTypeAsync();

        var createResponse = await _client.PostAsJsonAsync("/api/reservations", new CreateReservationRequest
        {
            TicketTypeId = ticketTypeId,
            Quantity = 1,
            IdempotencyKey = Guid.NewGuid(),
            CustomerIdentifier = "carol@example.com"
        });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var hold = await createResponse.Content.ReadFromJsonAsync<ReservationResponse>();

        var confirmResponse = await _client.PostAsJsonAsync(
            $"/api/reservations/{hold!.HoldId}/confirm",
            new ConfirmReservationRequest { CustomerIdentifier = "carol@example.com" });

        Assert.Equal(HttpStatusCode.OK, confirmResponse.StatusCode);
        var confirmBody = await confirmResponse.Content.ReadFromJsonAsync<JsonElement>();
        var bookingRef = confirmBody.GetProperty("bookingReference").GetString();
        Assert.NotNull(bookingRef);
        Assert.StartsWith("SP-", bookingRef);
    }

    [Fact]
    public async Task ConfirmHold_AlreadyConfirmed_Returns409()
    {
        var (ticketTypeId, _) = await GetFirstTicketTypeAsync();

        var createResponse = await _client.PostAsJsonAsync("/api/reservations", new CreateReservationRequest
        {
            TicketTypeId = ticketTypeId,
            Quantity = 1,
            IdempotencyKey = Guid.NewGuid(),
            CustomerIdentifier = "dave@example.com"
        });
        var hold = await createResponse.Content.ReadFromJsonAsync<ReservationResponse>();

        var firstConfirm = await _client.PostAsJsonAsync(
            $"/api/reservations/{hold!.HoldId}/confirm",
            new ConfirmReservationRequest { CustomerIdentifier = "dave@example.com" });
        Assert.Equal(HttpStatusCode.OK, firstConfirm.StatusCode);

        var secondConfirm = await _client.PostAsJsonAsync(
            $"/api/reservations/{hold.HoldId}/confirm",
            new ConfirmReservationRequest { CustomerIdentifier = "dave@example.com" });

        Assert.Equal(HttpStatusCode.Conflict, secondConfirm.StatusCode);
        var body = await secondConfirm.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("AlreadyConfirmed", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task ConfirmHold_Expired_Returns409()
    {
        var (ticketTypeId, _) = await GetFirstTicketTypeAsync();

        var createResponse = await _client.PostAsJsonAsync("/api/reservations", new CreateReservationRequest
        {
            TicketTypeId = ticketTypeId,
            Quantity = 1,
            IdempotencyKey = Guid.NewGuid(),
            CustomerIdentifier = "eve@example.com"
        });
        var hold = await createResponse.Content.ReadFromJsonAsync<ReservationResponse>();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StagePassDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE ReservationHolds SET ExpiresAt = {0} WHERE HoldId = {1}",
                DateTime.UtcNow.AddMinutes(-1), hold!.HoldId);
        }

        var confirmResponse = await _client.PostAsJsonAsync(
            $"/api/reservations/{hold!.HoldId}/confirm",
            new ConfirmReservationRequest { CustomerIdentifier = "eve@example.com" });

        Assert.Equal(HttpStatusCode.Conflict, confirmResponse.StatusCode);
        var body = await confirmResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("HoldExpired", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task ConfirmHold_WrongOwner_Returns409()
    {
        var (ticketTypeId, _) = await GetFirstTicketTypeAsync();

        var createResponse = await _client.PostAsJsonAsync("/api/reservations", new CreateReservationRequest
        {
            TicketTypeId = ticketTypeId,
            Quantity = 1,
            IdempotencyKey = Guid.NewGuid(),
            CustomerIdentifier = "alice@example.com"
        });
        var hold = await createResponse.Content.ReadFromJsonAsync<ReservationResponse>();

        var confirmResponse = await _client.PostAsJsonAsync(
            $"/api/reservations/{hold!.HoldId}/confirm",
            new ConfirmReservationRequest { CustomerIdentifier = "bob@example.com" });

        Assert.Equal(HttpStatusCode.Conflict, confirmResponse.StatusCode);
        var body = await confirmResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("InvalidOwner", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task GetBooking_ValidReference_Returns200()
    {
        var (ticketTypeId, _) = await GetFirstTicketTypeAsync();

        var createResponse = await _client.PostAsJsonAsync("/api/reservations", new CreateReservationRequest
        {
            TicketTypeId = ticketTypeId,
            Quantity = 2,
            IdempotencyKey = Guid.NewGuid(),
            CustomerIdentifier = "grace@example.com"
        });
        var hold = await createResponse.Content.ReadFromJsonAsync<ReservationResponse>();

        var confirmResponse = await _client.PostAsJsonAsync(
            $"/api/reservations/{hold!.HoldId}/confirm",
            new ConfirmReservationRequest { CustomerIdentifier = "grace@example.com" });
        var confirmBody = await confirmResponse.Content.ReadFromJsonAsync<JsonElement>();
        var bookingRef = confirmBody.GetProperty("bookingReference").GetString();

        var getResponse = await _client.GetAsync($"/api/bookings/{bookingRef}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var booking = await getResponse.Content.ReadFromJsonAsync<BookingDetailsDto>();
        Assert.NotNull(booking);
        Assert.Equal("Symphony Under the Stars", booking!.EventTitle);
        Assert.Equal(2, booking.Quantity);
    }

    [Fact]
    public async Task GetBooking_InvalidReference_Returns404()
    {
        var response = await _client.GetAsync("/api/bookings/INVALID");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ExpiredHold_WorkerRestoresInventory()
    {
        var (ticketTypeId, initialAvailable) = await GetFirstTicketTypeAsync();

        var createResponse = await _client.PostAsJsonAsync("/api/reservations", new CreateReservationRequest
        {
            TicketTypeId = ticketTypeId,
            Quantity = 3,
            IdempotencyKey = Guid.NewGuid(),
            CustomerIdentifier = "frank@example.com"
        });
        var hold = await createResponse.Content.ReadFromJsonAsync<ReservationResponse>();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StagePassDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE ReservationHolds SET ExpiresAt = {0} WHERE HoldId = {1}",
                DateTime.UtcNow.AddMinutes(-1), hold!.HoldId);

            var worker = scope.ServiceProvider.GetRequiredService<HoldExpiryWorker>();
            await worker.ExpireHoldsAsync(CancellationToken.None);
        }

        using (var verifyScope = _factory.Services.CreateScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<StagePassDbContext>();
            var holdInDb = await db.ReservationHolds.AsNoTracking().FirstAsync(h => h.HoldId == hold!.HoldId);
            var ttInDb = await db.TicketTypes.AsNoTracking().FirstAsync(t => t.TicketTypeId == ticketTypeId);

            Assert.Equal(ReservationStatus.Expired, holdInDb.Status);
            Assert.Equal(initialAvailable, ttInDb.Available);
        }
    }

    [Fact]
    public async Task InventoryInvariant_HoldsAfterMixedOperations()
    {
        var (ticketTypeId, _) = await GetFirstTicketTypeAsync();
        var holdIds = new List<int>();

        for (int i = 0; i < 5; i++)
        {
            var createResponse = await _client.PostAsJsonAsync("/api/reservations", new CreateReservationRequest
            {
                TicketTypeId = ticketTypeId,
                Quantity = 1,
                IdempotencyKey = Guid.NewGuid(),
                CustomerIdentifier = $"user{i}@example.com"
            });
            Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
            var hold = await createResponse.Content.ReadFromJsonAsync<ReservationResponse>();
            holdIds.Add(hold!.HoldId);
        }

        // Confirm 2
        for (int i = 0; i < 2; i++)
        {
            var confirmResponse = await _client.PostAsJsonAsync(
                $"/api/reservations/{holdIds[i]}/confirm",
                new ConfirmReservationRequest { CustomerIdentifier = $"user{i}@example.com" });
            Assert.Equal(HttpStatusCode.OK, confirmResponse.StatusCode);
        }

        // Expire 1 manually via worker
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StagePassDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE ReservationHolds SET ExpiresAt = {0} WHERE HoldId = {1}",
                DateTime.UtcNow.AddMinutes(-1), holdIds[2]);

            var worker = scope.ServiceProvider.GetRequiredService<HoldExpiryWorker>();
            await worker.ExpireHoldsAsync(CancellationToken.None);
        }

        using (var verifyScope = _factory.Services.CreateScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<StagePassDbContext>();
            var tt = await db.TicketTypes.AsNoTracking().FirstAsync(t => t.TicketTypeId == ticketTypeId);

            var pendingSum = await db.ReservationHolds
                .AsNoTracking()
                .Where(h => h.TicketTypeId == ticketTypeId && h.Status == ReservationStatus.Pending)
                .SumAsync(h => h.Quantity);

            var confirmedSum = await db.ReservationHolds
                .AsNoTracking()
                .Where(h => h.TicketTypeId == ticketTypeId && h.Status == ReservationStatus.Confirmed)
                .SumAsync(h => h.Quantity);

            Assert.Equal(tt.Capacity, tt.Available + pendingSum + confirmedSum);
        }
    }
}
