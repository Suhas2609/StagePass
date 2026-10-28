using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using StagePass.Api.DTOs.Reservations;
using StagePass.Api.Entities;
using StagePass.Api.Services;

namespace StagePass.Api.Controllers;

[ApiController]
[Route("api/reservations")]
public class ReservationsController : ControllerBase
{
    private readonly InventoryService _inventoryService;
    private readonly ReservationService _reservationService;

    public ReservationsController(InventoryService inventoryService, ReservationService reservationService)
    {
        _inventoryService = inventoryService;
        _reservationService = reservationService;
    }

    /// <summary>
    /// 201 Created for a newly created hold, 200 OK when an existing hold is returned for an idempotent retry.
    /// The new-vs-existing distinction comes from InventoryService.TryAllocateWithOutcomeAsync (IsNew flag).
    /// </summary>
    [HttpPost]
    [EnableRateLimiting("booking")]
    public async Task<IActionResult> CreateReservation([FromBody] CreateReservationRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var (result, isNew) = await _inventoryService.TryAllocateWithOutcomeAsync(request, ct);

        if (!result.Success)
        {
            return result.ErrorCode switch
            {
                ErrorCodes.InsufficientInventory or
                ErrorCodes.PerformanceCancelled or
                ErrorCodes.IdempotencyKeyReused => Conflict(new { error = result.ErrorCode }),
                _ => StatusCode(StatusCodes.Status500InternalServerError, new { error = "UnexpectedError" })
            };
        }

        // Reload through the service so the response can include the ticket type name.
        var hold = await _reservationService.GetHoldStatusAsync(result.Value!.HoldId, ct);
        if (hold is null)
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "UnexpectedError" });

        var response = ToResponse(hold);
        return isNew
            ? CreatedAtAction(nameof(GetReservation), new { holdId = hold.HoldId }, response)
            : Ok(response);
    }

    [HttpGet("{holdId:int}")]
    public async Task<ActionResult<ReservationResponse>> GetReservation(int holdId, CancellationToken ct)
    {
        var hold = await _reservationService.GetHoldStatusAsync(holdId, ct);
        if (hold is null)
            return NotFound();

        return Ok(ToResponse(hold));
    }

    [HttpPost("{holdId:int}/confirm")]
    public async Task<IActionResult> ConfirmReservation(
        int holdId, [FromBody] ConfirmReservationRequest request, CancellationToken ct)
    {
        var result = await _reservationService.ConfirmAsync(holdId, request.CustomerIdentifier, ct);

        if (result.Success)
            return Ok(new { bookingReference = result.Value!.BookingReference });

        return result.ErrorCode switch
        {
            ErrorCodes.HoldNotFound => NotFound(new { error = ErrorCodes.HoldNotFound }),
            ErrorCodes.HoldExpired or
            ErrorCodes.AlreadyConfirmed or
            ErrorCodes.InvalidOwner => Conflict(new { error = result.ErrorCode }),
            _ => StatusCode(StatusCodes.Status500InternalServerError, new { error = "UnexpectedError" })
        };
    }

    private static ReservationResponse ToResponse(ReservationHold hold) => new()
    {
        HoldId = hold.HoldId,
        TicketTypeId = hold.TicketTypeId,
        TicketTypeName = hold.TicketType.Name,
        Quantity = hold.Quantity,
        Status = hold.Status.ToString(),
        ExpiresAt = hold.ExpiresAt,
        CreatedAt = hold.CreatedAt
    };
}
