using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StagePass.Api.BackgroundServices;
using StagePass.Api.Data;
using StagePass.Api.DTOs.Reservations;
using StagePass.Api.Enums;
using StagePass.Api.Services;

namespace StagePass.Tests.IntegrationTests;

[Collection("DatabaseIntegrationTests")]
[Trait("Category", "RequiresSqlServer")]
public class ReservationConcurrencyTests : IAsyncLifetime
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public ReservationConcurrencyTests(TestWebApplicationFactory factory)
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

    [Fact]
    public async Task ConcurrentReservations_FiveHundredRequests_OneHundredTickets()
    {
        int ticketTypeId;
        using (var setupScope = _factory.Services.CreateScope())
        {
            var db = setupScope.ServiceProvider.GetRequiredService<StagePassDbContext>();
            var ticketType = await db.TicketTypes.AsNoTracking().OrderBy(t => t.TicketTypeId).FirstAsync();
            ticketTypeId = ticketType.TicketTypeId;

            await db.Database.ExecuteSqlRawAsync(
                "UPDATE TicketTypes SET Capacity = 100, Available = 100 WHERE TicketTypeId = {0}",
                ticketTypeId);
        }

        var keys = Enumerable.Range(0, 500).Select(_ => Guid.NewGuid()).ToArray();

        var tasks = Enumerable.Range(0, 500).Select(i =>
            _client.PostAsJsonAsync("/api/reservations", new
            {
                ticketTypeId = ticketTypeId,
                quantity = 1,
                idempotencyKey = keys[i],
                customerIdentifier = $"user{i}@example.com"
            })
        ).ToList();

        var responses = await Task.WhenAll(tasks);

        int successCount = responses.Count(r => r.StatusCode == HttpStatusCode.Created);
        int conflictCount = responses.Count(r => r.StatusCode == HttpStatusCode.Conflict);
        int errorCount = responses.Count(r => (int)r.StatusCode >= 500);

        Assert.Equal(100, successCount);    // exactly 100 tickets sold
        Assert.Equal(400, conflictCount);   // exactly 400 rejections
        Assert.Equal(0, errorCount);        // NO server errors

        // Verify conflict responses have the correct error code
        foreach (var r in responses.Where(r => r.StatusCode == HttpStatusCode.Conflict))
        {
            var body = await r.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("InsufficientInventory", body.GetProperty("error").GetString());
        }

        // Verify DB invariant
        using var scope = _factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<StagePassDbContext>();
        var tt = await ctx.TicketTypes.FindAsync(ticketTypeId);
        Assert.NotNull(tt);
        Assert.Equal(0, tt!.Available);     // all tickets allocated

        var activeSold = await ctx.ReservationHolds
            .Where(h => h.TicketTypeId == ticketTypeId &&
                        (h.Status == ReservationStatus.Pending ||
                         h.Status == ReservationStatus.Confirmed))
            .SumAsync(h => h.Quantity);
        Assert.Equal(100, activeSold);      // exactly 100 units in active holds

        // Final invariant
        Assert.Equal(tt.Capacity, tt.Available + activeSold);  // 100 == 0 + 100
    }

    [Fact]
    public async Task ConfirmVersusExpiry_Race_MaintainsInventoryInvariant()
    {
        int ticketTypeId;
        var holdIds = new List<int>();

        using (var setupScope = _factory.Services.CreateScope())
        {
            var db = setupScope.ServiceProvider.GetRequiredService<StagePassDbContext>();
            var inventoryService = setupScope.ServiceProvider.GetRequiredService<InventoryService>();

            var ticketType = await db.TicketTypes.AsNoTracking().OrderBy(t => t.TicketTypeId).FirstAsync();
            ticketTypeId = ticketType.TicketTypeId;

            await db.Database.ExecuteSqlRawAsync(
                "UPDATE TicketTypes SET Capacity = 100, Available = 100 WHERE TicketTypeId = {0}",
                ticketTypeId);

            for (int i = 0; i < 20; i++)
            {
                var alloc = await inventoryService.TryAllocateAsync(new CreateReservationRequest
                {
                    TicketTypeId = ticketTypeId,
                    Quantity = 2,
                    IdempotencyKey = Guid.NewGuid(),
                    CustomerIdentifier = $"race-customer-{i}@example.com"
                }, CancellationToken.None);

                Assert.True(alloc.Success);
                holdIds.Add(alloc.Value!.HoldId);
            }

            foreach (var expiredHoldId in holdIds.Take(10))
            {
                await db.Database.ExecuteSqlRawAsync(
                    "UPDATE ReservationHolds SET ExpiresAt = DATEADD(second, -2, GETUTCDATE()) WHERE HoldId = {0}",
                    expiredHoldId);
            }
        }

        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var unexpectedExceptions = new ConcurrentBag<Exception>();

        var confirmTasks = holdIds.Select(async (holdId, idx) =>
        {
            await startGate.Task;
            try
            {
                await using var scope = _factory.Services.CreateAsyncScope();
                var reservationService = scope.ServiceProvider.GetRequiredService<ReservationService>();
                await reservationService.ConfirmAsync(holdId, $"race-customer-{idx}@example.com", CancellationToken.None);
            }
            catch (Exception ex)
            {
                unexpectedExceptions.Add(ex);
            }
        });

        var expiryTasks = Enumerable.Range(0, 5).Select(async _ =>
        {
            await startGate.Task;
            try
            {
                using var scope = _factory.Services.CreateScope();
                var worker = scope.ServiceProvider.GetRequiredService<HoldExpiryWorker>();
                await worker.ExpireHoldsAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                unexpectedExceptions.Add(ex);
            }
        });

        startGate.SetResult();
        await Task.WhenAll(confirmTasks.Concat(expiryTasks));

        Assert.Empty(unexpectedExceptions);

        using (var verifyScope = _factory.Services.CreateScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<StagePassDbContext>();

            var tt = await db.TicketTypes.AsNoTracking().FirstAsync(t => t.TicketTypeId == ticketTypeId);
            var holds = await db.ReservationHolds
                .AsNoTracking()
                .Where(h => h.TicketTypeId == ticketTypeId)
                .ToListAsync();
            var bookings = await db.Bookings.AsNoTracking().ToListAsync();

            int pendingQty = holds.Where(h => h.Status == ReservationStatus.Pending).Sum(h => h.Quantity);
            int confirmedQty = holds.Where(h => h.Status == ReservationStatus.Confirmed).Sum(h => h.Quantity);
            int expiredQty = holds.Where(h => h.Status == ReservationStatus.Expired).Sum(h => h.Quantity);

            Assert.Equal(0, pendingQty);
            Assert.Equal(20, confirmedQty);
            Assert.Equal(20, expiredQty);
            Assert.Equal(10, bookings.Count);
            Assert.Equal(80, tt.Available);
            Assert.Equal(tt.Capacity, tt.Available + pendingQty + confirmedQty);
        }
    }

    [Fact]
    public async Task CancelPerformanceVersusExpiry_Race_RestoresInventoryExactlyOnce()
    {
        int performanceId;
        int ticketTypeId;

        using (var setupScope = _factory.Services.CreateScope())
        {
            var db = setupScope.ServiceProvider.GetRequiredService<StagePassDbContext>();
            var inventoryService = setupScope.ServiceProvider.GetRequiredService<InventoryService>();

            var ticketType = await db.TicketTypes.AsNoTracking().OrderBy(t => t.TicketTypeId).FirstAsync();
            ticketTypeId = ticketType.TicketTypeId;
            performanceId = ticketType.PerformanceId;

            await db.Database.ExecuteSqlRawAsync(
                "UPDATE TicketTypes SET Capacity = 100, Available = 100 WHERE TicketTypeId = {0}",
                ticketTypeId);

            for (int i = 0; i < 20; i++)
            {
                var alloc = await inventoryService.TryAllocateAsync(new CreateReservationRequest
                {
                    TicketTypeId = ticketTypeId,
                    Quantity = 2,
                    IdempotencyKey = Guid.NewGuid(),
                    CustomerIdentifier = $"cancel-race-{i}@example.com"
                }, CancellationToken.None);
                Assert.True(alloc.Success);
            }

            await db.Database.ExecuteSqlRawAsync(
                "UPDATE ReservationHolds SET ExpiresAt = DATEADD(second, -5, GETUTCDATE()) WHERE TicketTypeId = {0}",
                ticketTypeId);
        }

        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var unexpectedExceptions = new ConcurrentBag<Exception>();

        var cancelTask = Task.Run(async () =>
        {
            await startGate.Task;
            try
            {
                await using var scope = _factory.Services.CreateAsyncScope();
                var reservationService = scope.ServiceProvider.GetRequiredService<ReservationService>();
                await reservationService.CancelPerformanceAsync(performanceId, CancellationToken.None);
            }
            catch (Exception ex)
            {
                unexpectedExceptions.Add(ex);
            }
        });

        var expiryTasks = Enumerable.Range(0, 5).Select(_ => Task.Run(async () =>
        {
            await startGate.Task;
            try
            {
                using var scope = _factory.Services.CreateScope();
                var worker = scope.ServiceProvider.GetRequiredService<HoldExpiryWorker>();
                await worker.ExpireHoldsAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                unexpectedExceptions.Add(ex);
            }
        }));

        startGate.SetResult();
        await Task.WhenAll(expiryTasks.Append(cancelTask));

        Assert.Empty(unexpectedExceptions);

        using (var verifyScope = _factory.Services.CreateScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<StagePassDbContext>();

            var tt = await db.TicketTypes.AsNoTracking().FirstAsync(t => t.TicketTypeId == ticketTypeId);
            var holds = await db.ReservationHolds
                .AsNoTracking()
                .Where(h => h.TicketTypeId == ticketTypeId)
                .ToListAsync();

            int pendingQty = holds.Where(h => h.Status == ReservationStatus.Pending).Sum(h => h.Quantity);
            int confirmedQty = holds.Where(h => h.Status == ReservationStatus.Confirmed).Sum(h => h.Quantity);

            Assert.Equal(0, pendingQty);
            Assert.Equal(100, tt.Available);
            Assert.Equal(tt.Capacity, tt.Available + pendingQty + confirmedQty);
        }
    }
}
