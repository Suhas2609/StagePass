using Microsoft.EntityFrameworkCore;
using StagePass.Api.Data;
using StagePass.Api.DTOs.Bookings;

namespace StagePass.Api.Services;

public class BookingService
{
    private readonly StagePassDbContext _context;

    public BookingService(StagePassDbContext context)
    {
        _context = context;
    }

    public async Task<BookingDetailsDto?> GetByReferenceAsync(string reference, CancellationToken ct = default)
    {
        return await _context.Bookings
            .AsNoTracking()
            .Include(b => b.ReservationHold)
                .ThenInclude(rh => rh.TicketType)
                    .ThenInclude(tt => tt.Performance)
                        .ThenInclude(p => p.Event)
                            .ThenInclude(e => e.Venue)
            .Where(b => b.BookingReference == reference)
            .Select(booking => new BookingDetailsDto
            {
                BookingReference         = booking.BookingReference,
                EventTitle               = booking.ReservationHold.TicketType.Performance.Event.Title,
                PerformanceStartDateTime = booking.ReservationHold.TicketType.Performance.StartDateTime,
                VenueName                = booking.ReservationHold.TicketType.Performance.Event.Venue.Name,
                TicketTypeName           = booking.ReservationHold.TicketType.Name,
                Quantity                 = booking.ReservationHold.Quantity,
                TotalPrice               = booking.ReservationHold.Quantity * booking.ReservationHold.TicketType.Price,
                Status                   = booking.Status,
                CreatedAt                = booking.CreatedAt
            })
            .FirstOrDefaultAsync(ct);
    }
}
