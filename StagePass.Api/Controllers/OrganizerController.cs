using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StagePass.Api.Data;
using StagePass.Api.DTOs.Organizers;
using StagePass.Api.Entities;
using StagePass.Api.Enums;
using StagePass.Api.Security;
using StagePass.Api.Services;

namespace StagePass.Api.Controllers;

/// <summary>
/// Organizer endpoints. Require a valid session (Authorization: Bearer token from /api/auth/login) of a user
/// with IsOrganizer = true (see <see cref="RequireOrganizerAttribute"/>).
/// MVP limitation: AppUser is linked to the legacy Organizers table by email only, and there is no per-event
/// ownership check on performances/ticket types/bookings/cancel - any organizer can act on any event's
/// performances. Events created through this API are always owned by the caller's Organizer record.
/// </summary>
[ApiController]
[Route("api/organizer")]
[RequireOrganizer]
public class OrganizerController : ControllerBase
{
    private readonly StagePassDbContext _context;
    private readonly ReservationService _reservationService;

    public OrganizerController(StagePassDbContext context, ReservationService reservationService)
    {
        _context = context;
        _reservationService = reservationService;
    }

    [HttpGet("venues")]
    public async Task<IActionResult> GetVenues(CancellationToken ct)
    {
        var venues = await _context.Venues
            .AsNoTracking()
            .OrderBy(v => v.Name)
            .Select(v => new { v.VenueId, v.Name, v.City })
            .ToListAsync(ct);
        return Ok(venues);
    }

    [HttpPost("events")]
    public async Task<IActionResult> CreateEvent([FromBody] CreateEventRequest request, CancellationToken ct)
    {
        // request.OrganizerId is ignored: the event is owned by the Organizer record linked (by email) to the
        // authenticated user, so a caller cannot create events on behalf of another organizer.
        var user = (AppUser)HttpContext.Items["CurrentUser"]!;
        var organizer = await _context.Organizers
            .FirstOrDefaultAsync(o => o.Email == user.Email, ct);

        if (organizer is null)
        {
            // Auto-provision domain Organizer record for authenticated organizers
            organizer = new Organizer
            {
                Name = user.Name,
                Email = user.Email
            };
            _context.Organizers.Add(organizer);
            await _context.SaveChangesAsync(ct);
        }

        if (!await _context.Venues.AnyAsync(v => v.VenueId == request.VenueId, ct))
            return BadRequest(new { error = "VenueNotFound" });

        var newEvent = new Event
        {
            OrganizerId = organizer.OrganizerId,
            VenueId = request.VenueId,
            Title = request.Title,
            Category = request.Category,
            Description = request.Description
        };
        _context.Events.Add(newEvent);
        await _context.SaveChangesAsync(ct);

        return Created($"/api/organizer/events/{newEvent.EventId}", new { eventId = newEvent.EventId });
    }

    [HttpPost("performances")]
    public async Task<IActionResult> CreatePerformance([FromBody] CreatePerformanceRequest request, CancellationToken ct)
    {
        // Normalize to UTC: values without an offset are treated as UTC, local values are converted.
        var start = request.StartDateTime.Kind switch
        {
            DateTimeKind.Local => request.StartDateTime.ToUniversalTime(),
            DateTimeKind.Unspecified => DateTime.SpecifyKind(request.StartDateTime, DateTimeKind.Utc),
            _ => request.StartDateTime
        };

        if (start <= DateTime.UtcNow)
            return BadRequest(new { error = "StartDateTimeMustBeInFuture" });

        if (!await _context.Events.AnyAsync(e => e.EventId == request.EventId, ct))
            return NotFound(new { error = "EventNotFound" });

        var performance = new Performance
        {
            EventId = request.EventId,
            StartDateTime = start,
            Status = PerformanceStatus.Upcoming
        };
        _context.Performances.Add(performance);
        await _context.SaveChangesAsync(ct);

        return Created($"/api/performances/{performance.PerformanceId}", new { performanceId = performance.PerformanceId });
    }

    [HttpPost("performances/{id:int}/tickettypes")]
    public async Task<IActionResult> CreateTicketType(int id, [FromBody] CreateTicketTypeRequest request, CancellationToken ct)
    {
        if (!await _context.Performances.AnyAsync(p => p.PerformanceId == id, ct))
            return NotFound(new { error = "PerformanceNotFound" });

        if (await _context.TicketTypes.AnyAsync(tt => tt.PerformanceId == id && tt.Name == request.Name, ct))
            return Conflict(new { error = "TicketTypeNameAlreadyExists" });

        var ticketType = new TicketType
        {
            PerformanceId = id,
            Name = request.Name,
            Price = request.Price,
            Capacity = request.Capacity,
            Available = request.Capacity   // never taken from the request body
        };
        _context.TicketTypes.Add(ticketType);

        try
        {
            await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Possible race with the unique index (PerformanceId, Name) between the check above and the insert.
            _context.ChangeTracker.Clear();
            if (await _context.TicketTypes.AsNoTracking()
                    .AnyAsync(tt => tt.PerformanceId == id && tt.Name == request.Name, ct))
                return Conflict(new { error = "TicketTypeNameAlreadyExists" });
            throw;
        }

        return Created($"/api/performances/{id}", new { ticketTypeId = ticketType.TicketTypeId });
    }

    [HttpGet("performances/{id:int}/bookings")]
    public async Task<IActionResult> GetPerformanceBookings(int id, CancellationToken ct)
    {
        if (!await _context.Performances.AnyAsync(p => p.PerformanceId == id, ct))
            return NotFound(new { error = "PerformanceNotFound" });

        var bookings = await _context.Bookings
            .AsNoTracking()
            .Where(b => b.Status == "Confirmed" && b.ReservationHold.TicketType.PerformanceId == id)
            .OrderBy(b => b.CreatedAt)
            .Select(b => new OrganizerBookingDto
            {
                BookingReference = b.BookingReference,
                CustomerIdentifier = b.CustomerIdentifier,
                TicketTypeName = b.ReservationHold.TicketType.Name,
                Quantity = b.ReservationHold.Quantity,
                BookingCreatedAt = b.CreatedAt
            })
            .ToListAsync(ct);

        // sold = Capacity - Available (so it includes quantities currently held as Pending).
        var summary = await _context.TicketTypes
            .AsNoTracking()
            .Where(tt => tt.PerformanceId == id)
            .OrderBy(tt => tt.TicketTypeId)
            .Select(tt => new
            {
                ticketTypeName = tt.Name,
                capacity = tt.Capacity,
                available = tt.Available,
                sold = tt.Capacity - tt.Available
            })
            .ToListAsync(ct);

        return Ok(new { bookings, summary });
    }

    [HttpPatch("performances/{id:int}/cancel")]
    public async Task<IActionResult> CancelPerformance(int id, CancellationToken ct)
    {
        if (!await _context.Performances.AnyAsync(p => p.PerformanceId == id, ct))
            return NotFound(new { error = "PerformanceNotFound" });

        var result = await _reservationService.CancelPerformanceAsync(id, ct);
        if (result.Success)
            return Ok(new { status = "Cancelled" });

        return Conflict(new { error = ErrorCodes.PerformanceCancelled });
    }
}
