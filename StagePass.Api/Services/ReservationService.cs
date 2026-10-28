using Microsoft.EntityFrameworkCore;
using StagePass.Api.Data;
using StagePass.Api.Entities;
using StagePass.Api.Enums;

namespace StagePass.Api.Services;

public class ReservationService
{
    private readonly StagePassDbContext _context;

    private sealed class ReleasedHoldRow
    {
        public int HoldId { get; set; }
        public int TicketTypeId { get; set; }
        public int Quantity { get; set; }
    }

    public ReservationService(StagePassDbContext context)
    {
        _context = context;
    }

    public Task<ReservationHold?> GetHoldStatusAsync(int holdId, CancellationToken ct = default)
    {
        return _context.ReservationHolds
            .Include(h => h.TicketType)
            .FirstOrDefaultAsync(h => h.HoldId == holdId, ct);
    }

    public async Task<ServiceResult<Booking>> ConfirmAsync(
        int holdId, string customerIdentifier, CancellationToken ct = default)
    {
        await using var tx = await _context.Database.BeginTransactionAsync(ct);
        try
        {
            int rows = await _context.Database.ExecuteSqlRawAsync(
                @"UPDATE ReservationHolds SET Status = 'Confirmed'
                  WHERE HoldId = {0}
                    AND Status = 'Pending'
                    AND ExpiresAt > GETUTCDATE()",
                new object[] { holdId }, ct);

            if (rows == 0)
            {
                await tx.RollbackAsync(ct);
                // Distinguish HoldNotFound / AlreadyConfirmed / HoldExpired
                var hold = await _context.ReservationHolds
                    .AsNoTracking()
                    .FirstOrDefaultAsync(h => h.HoldId == holdId, ct);
                if (hold == null)
                    return ServiceResult<Booking>.Fail(ErrorCodes.HoldNotFound);
                if (hold.Status == ReservationStatus.Confirmed)
                    return ServiceResult<Booking>.Fail(ErrorCodes.AlreadyConfirmed);
                return ServiceResult<Booking>.Fail(ErrorCodes.HoldExpired);
            }

            // Ownership check — reload the hold inside the transaction
            var confirmedHold = await _context.ReservationHolds
                .AsNoTracking()
                .FirstAsync(h => h.HoldId == holdId, ct);
            if (!confirmedHold.CustomerIdentifier.Equals(customerIdentifier,
                  StringComparison.OrdinalIgnoreCase))
            {
                await tx.RollbackAsync(ct);
                return ServiceResult<Booking>.Fail(ErrorCodes.InvalidOwner);
            }

            var booking = new Booking
            {
                BookingReference   = GenerateReference(),
                HoldId             = holdId,
                CustomerIdentifier = customerIdentifier,
                Status             = "Confirmed",
                CreatedAt          = DateTime.UtcNow,
            };
            _context.Bookings.Add(booking);
            await _context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ServiceResult<Booking>.Ok(booking);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    private static string GenerateReference() =>
        "SP-" + Guid.NewGuid().ToString("N")[..8].ToUpper();

    public async Task<ServiceResult<bool>> CancelPerformanceAsync(
        int performanceId, CancellationToken ct = default)
    {
        await using var tx = await _context.Database.BeginTransactionAsync(ct);
        try
        {
            int perfRows = await _context.Database.ExecuteSqlRawAsync(
                "UPDATE Performances SET Status = 'Cancelled' WHERE PerformanceId = {0} AND Status = 'Upcoming'",
                new object[] { performanceId }, ct);

            if (perfRows == 0)
            {
                await tx.RollbackAsync(ct);
                return ServiceResult<bool>.Fail(ErrorCodes.PerformanceCancelled);
            }

            // Atomically cancel Pending holds for this performance and capture ONLY the holds
            // that transitioned from 'Pending' to 'Cancelled' via SQL Server UPDATE ... OUTPUT INTO.
            var cancelledHolds = await _context.Database
                .SqlQueryRaw<ReleasedHoldRow>(
                    @"DECLARE @Cancelled TABLE (
                          HoldId int NOT NULL,
                          TicketTypeId int NOT NULL,
                          Quantity int NOT NULL
                      );

                      UPDATE rh
                      SET rh.Status = 'Cancelled'
                      OUTPUT inserted.HoldId, inserted.TicketTypeId, inserted.Quantity
                      INTO @Cancelled (HoldId, TicketTypeId, Quantity)
                      FROM ReservationHolds rh
                      INNER JOIN TicketTypes tt ON rh.TicketTypeId = tt.TicketTypeId
                      WHERE tt.PerformanceId = {0}
                        AND rh.Status = 'Pending';

                      SELECT HoldId, TicketTypeId, Quantity FROM @Cancelled;",
                    new object[] { performanceId })
                .ToListAsync(ct);

            if (cancelledHolds.Any())
            {
                // Aggregate by TicketTypeId and restore inventory
                var release = cancelledHolds
                    .GroupBy(h => h.TicketTypeId)
                    .Select(g => new { TicketTypeId = g.Key, Total = g.Sum(h => h.Quantity) })
                    .OrderBy(r => r.TicketTypeId);

                foreach (var r in release)
                    await _context.Database.ExecuteSqlRawAsync(
                        "UPDATE TicketTypes SET Available = Available + {0} WHERE TicketTypeId = {1}",
                        new object[] { r.Total, r.TicketTypeId }, ct);
            }

            await _context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ServiceResult<bool>.Ok(true);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }
}
