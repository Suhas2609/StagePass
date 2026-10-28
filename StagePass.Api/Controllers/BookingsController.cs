using Microsoft.AspNetCore.Mvc;
using StagePass.Api.DTOs.Bookings;
using StagePass.Api.Services;

namespace StagePass.Api.Controllers;

[ApiController]
[Route("api/bookings")]
public class BookingsController : ControllerBase
{
    private readonly BookingService _bookingService;

    public BookingsController(BookingService bookingService)
    {
        _bookingService = bookingService;
    }

    [HttpGet("{reference}")]
    public async Task<ActionResult<BookingDetailsDto>> GetBooking(string reference, CancellationToken ct)
    {
        var booking = await _bookingService.GetByReferenceAsync(reference, ct);
        if (booking is null)
            return NotFound();

        return Ok(booking);
    }
}
