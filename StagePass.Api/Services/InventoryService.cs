using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using StagePass.Api.Data;
using StagePass.Api.DTOs.Reservations;
using StagePass.Api.Entities;
using StagePass.Api.Enums;

namespace StagePass.Api.Services;

public class InventoryService
{
    private readonly StagePassDbContext _context;

    public InventoryService(StagePassDbContext context)
    {
        _context = context;
    }

    public async Task<ServiceResult<ReservationHold>> TryAllocateAsync(
        CreateReservationRequest request, CancellationToken ct = default)
    {
        var (result, _) = await TryAllocateWithOutcomeAsync(request, ct);
        return result;
    }

    // Same behaviour as TryAllocateAsync, but also reports whether a NEW hold was created (IsNew = true)
    // or an existing hold was returned for an idempotent retry (IsNew = false). Used by the controller
    // to choose between 201 Created and 200 OK.
    public async Task<(ServiceResult<ReservationHold> Result, bool IsNew)> TryAllocateWithOutcomeAsync(
        CreateReservationRequest request, CancellationToken ct = default)
    {
        // STEP 2A - IDEMPOTENCY PRE-CHECK (no transaction, read-only)
        var existing = await _context.ReservationHolds
            .AsNoTracking()
            .FirstOrDefaultAsync(h => h.IdempotencyKey == request.IdempotencyKey, ct);

        if (existing != null)
        {
            if (existing.TicketTypeId == request.TicketTypeId &&
                existing.Quantity == request.Quantity)
                return (ServiceResult<ReservationHold>.Ok(existing), false); // legitimate retry

            return (ServiceResult<ReservationHold>.Fail(ErrorCodes.IdempotencyKeyReused), false);
        }

        // STEP 2B - ATOMIC ALLOCATION (one explicit transaction)
        await using var tx = await _context.Database.BeginTransactionAsync(ct);
        try
        {
            // Atomic conditional UPDATE - rows affected is the return value
            int rows = await _context.Database.ExecuteSqlRawAsync(
                @"UPDATE TicketTypes
                  SET Available = Available - {0}
                  WHERE TicketTypeId = {1}
                    AND Available >= {0}
                    AND PerformanceId IN (
                        SELECT PerformanceId FROM Performances WHERE Status = 'Upcoming'
                    )",
                new object[] { request.Quantity, request.TicketTypeId },
                ct);

            if (rows == 0)
            {
                await tx.RollbackAsync(ct);

                // If a concurrent request with the same IdempotencyKey just consumed the remaining inventory,
                // return the idempotent result rather than InsufficientInventory.
                var concurrentHold = await _context.ReservationHolds
                    .AsNoTracking()
                    .FirstOrDefaultAsync(h => h.IdempotencyKey == request.IdempotencyKey, ct);
                if (concurrentHold != null)
                {
                    if (concurrentHold.TicketTypeId == request.TicketTypeId &&
                        concurrentHold.Quantity == request.Quantity)
                        return (ServiceResult<ReservationHold>.Ok(concurrentHold), false);

                    return (ServiceResult<ReservationHold>.Fail(ErrorCodes.IdempotencyKeyReused), false);
                }

                // Determine whether the cause is InsufficientInventory or PerformanceCancelled
                var ticketType = await _context.TicketTypes
                    .AsNoTracking()
                    .Include(tt => tt.Performance)
                    .FirstOrDefaultAsync(tt => tt.TicketTypeId == request.TicketTypeId, ct);
                if (ticketType == null || ticketType.Performance.Status == PerformanceStatus.Cancelled)
                    return (ServiceResult<ReservationHold>.Fail(ErrorCodes.PerformanceCancelled), false);
                return (ServiceResult<ReservationHold>.Fail(ErrorCodes.InsufficientInventory), false);
            }

            var hold = new ReservationHold
            {
                IdempotencyKey     = request.IdempotencyKey,
                CustomerIdentifier = request.CustomerIdentifier,
                TicketTypeId       = request.TicketTypeId,
                Quantity           = request.Quantity,
                Status             = ReservationStatus.Pending,
                CreatedAt          = DateTime.UtcNow,
                ExpiresAt          = DateTime.UtcNow.AddMinutes(5),
            };
            _context.ReservationHolds.Add(hold);
            await _context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return (ServiceResult<ReservationHold>.Ok(hold), true);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            // Race: two concurrent requests with the same key both passed the pre-check
            await tx.RollbackAsync(ct);
            _context.ChangeTracker.Clear();
            var existingAfterRace = await _context.ReservationHolds
                .AsNoTracking()
                .FirstOrDefaultAsync(h => h.IdempotencyKey == request.IdempotencyKey, ct);
            if (existingAfterRace is not null)
            {
                if (existingAfterRace.TicketTypeId == request.TicketTypeId &&
                    existingAfterRace.Quantity == request.Quantity)
                    return (ServiceResult<ReservationHold>.Ok(existingAfterRace), false);

                return (ServiceResult<ReservationHold>.Fail(ErrorCodes.IdempotencyKeyReused), false);
            }
            return (ServiceResult<ReservationHold>.Fail(ErrorCodes.InsufficientInventory), false);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        if (ex.InnerException is SqlException sqlEx && (sqlEx.Number == 2627 || sqlEx.Number == 2601))
            return true;

        return ex.InnerException?.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) == true;
    }
}

