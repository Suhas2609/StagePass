using Microsoft.EntityFrameworkCore;
using StagePass.Api.Data;

namespace StagePass.Api.BackgroundServices;

public class HoldExpiryWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<HoldExpiryWorker> _logger;

    private sealed class ReleasedHoldRow
    {
        public int HoldId { get; set; }
        public int TicketTypeId { get; set; }
        public int Quantity { get; set; }
    }

    public HoldExpiryWorker(
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        ILogger<HoldExpiryWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        int intervalSeconds = int.TryParse(_config["HoldExpiry:IntervalSeconds"], out var parsed) && parsed > 0
            ? parsed
            : 60;

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(intervalSeconds));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await ExpireHoldsAsync(stoppingToken);
        }
    }

    public async Task ExpireHoldsAsync(CancellationToken ct = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<StagePassDbContext>();
        await using var tx = await context.Database.BeginTransactionAsync(ct);
        try
        {
            // Step 1: Atomically transition eligible Pending holds to Expired and capture
            // ONLY the holds that actually transitioned via SQL Server UPDATE ... OUTPUT INTO.
            var expiredHolds = await context.Database
                .SqlQueryRaw<ReleasedHoldRow>(
                    @"DECLARE @Expired TABLE (
                          HoldId int NOT NULL,
                          TicketTypeId int NOT NULL,
                          Quantity int NOT NULL
                      );

                      UPDATE ReservationHolds
                      SET Status = 'Expired'
                      OUTPUT inserted.HoldId, inserted.TicketTypeId, inserted.Quantity
                      INTO @Expired (HoldId, TicketTypeId, Quantity)
                      WHERE Status = 'Pending'
                        AND ExpiresAt <= GETUTCDATE();

                      SELECT HoldId, TicketTypeId, Quantity FROM @Expired;")
                .ToListAsync(ct);

            if (!expiredHolds.Any())
            {
                await tx.CommitAsync(ct);
                return;
            }

            int expiredCount = expiredHolds.Count;

            // Step 2: Aggregate released quantities by TicketTypeId
            var release = expiredHolds
                .GroupBy(h => h.TicketTypeId)
                .Select(g => new { TicketTypeId = g.Key, Total = g.Sum(h => h.Quantity) })
                .OrderBy(r => r.TicketTypeId);

            // Step 3: Restore inventory
            foreach (var r in release)
                await context.Database.ExecuteSqlRawAsync(
                    "UPDATE TicketTypes SET Available = Available + {0} WHERE TicketTypeId = {1}",
                    new object[] { r.Total, r.TicketTypeId }, ct);

            await tx.CommitAsync(ct);
            _logger.LogInformation("Hold expiry: expired {Count} hold(s)", expiredCount);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            _logger.LogError(ex, "Hold expiry failed; will retry on next tick");
            // Do not rethrow — allow the loop to continue
        }
    }
}
