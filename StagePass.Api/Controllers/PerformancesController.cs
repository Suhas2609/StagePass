using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StagePass.Api.Data;
using StagePass.Api.DTOs.Performances;
using StagePass.Api.Enums;

namespace StagePass.Api.Controllers;

[ApiController]
[Route("api/performances")]
public class PerformancesController : ControllerBase
{
    private readonly StagePassDbContext _context;

    public PerformancesController(StagePassDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<List<PerformanceListItemDto>>> GetPerformances(
        [FromQuery] string? category,
        [FromQuery] string? date,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken ct = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 50);

        var query = _context.Performances
            .AsNoTracking()
            .Where(p => p.Status == PerformanceStatus.Upcoming);

        if (!string.IsNullOrWhiteSpace(search))
        {
            // Case-insensitive "contains" match on the event title (used by the frontend search box).
            var term = search.Trim().ToLower();
            query = query.Where(p => p.Event.Title.ToLower().Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            var normalizedCategory = category.Trim().ToLower();
            query = query.Where(p => p.Event.Category.ToLower() == normalizedCategory);
        }

        if (!string.IsNullOrWhiteSpace(date))
        {
            if (!DateOnly.TryParse(date, out var parsedDate))
                return BadRequest(new { error = "InvalidDate", message = "date must be a valid date, e.g. 2026-12-31." });

            var dayStart = parsedDate.ToDateTime(TimeOnly.MinValue);
            var dayEnd = dayStart.AddDays(1);
            query = query.Where(p => p.StartDateTime >= dayStart && p.StartDateTime < dayEnd);
        }

        // Status is read as the enum and converted in memory to keep the SQL translation simple.
        var rows = await query
            .OrderBy(p => p.StartDateTime)
            .ThenBy(p => p.PerformanceId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new
            {
                p.PerformanceId,
                EventTitle = p.Event.Title,
                Category = p.Event.Category,
                VenueName = p.Event.Venue.Name,
                City = p.Event.Venue.City,
                p.StartDateTime,
                p.Status,
                LowestPrice = p.TicketTypes.Min(tt => (decimal?)tt.Price) ?? 0,
                TotalAvailable = p.TicketTypes.Sum(tt => tt.Available)
            })
            .ToListAsync(ct);

        var items = rows.Select(r => new PerformanceListItemDto
        {
            PerformanceId = r.PerformanceId,
            EventTitle = r.EventTitle,
            Category = r.Category,
            VenueName = r.VenueName,
            City = r.City,
            StartDateTime = r.StartDateTime,
            Status = r.Status.ToString(),
            LowestPrice = r.LowestPrice,
            TotalAvailable = r.TotalAvailable
        }).ToList();

        return Ok(items);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<PerformanceDetailsDto>> GetPerformance(int id, CancellationToken ct)
    {
        var performance = await _context.Performances
            .AsNoTracking()
            .Include(p => p.Event).ThenInclude(e => e.Venue)
            .Include(p => p.TicketTypes)
            .FirstOrDefaultAsync(p => p.PerformanceId == id, ct);

        if (performance is null || performance.Status != PerformanceStatus.Upcoming)
            return NotFound();

        return Ok(new PerformanceDetailsDto
        {
            PerformanceId = performance.PerformanceId,
            EventTitle = performance.Event.Title,
            EventDescription = performance.Event.Description,
            Category = performance.Event.Category,
            VenueName = performance.Event.Venue.Name,
            VenueAddress = performance.Event.Venue.Address,
            City = performance.Event.Venue.City,
            StartDateTime = performance.StartDateTime,
            Status = performance.Status.ToString(),
            TicketTypes = performance.TicketTypes
                .OrderBy(tt => tt.TicketTypeId)
                .Select(tt => new TicketTypeDto
                {
                    TicketTypeId = tt.TicketTypeId,
                    Name = tt.Name,
                    Price = tt.Price,
                    Available = tt.Available
                }).ToList()
        });
    }
}
