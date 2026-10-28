using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StagePass.Api.Data;
using StagePass.Api.Entities;
using StagePass.Api.Enums;

namespace StagePass.Api.Controllers;

// Development-only demo controller for live concurrency demonstration.
// Excluded from the MVC application model outside Development in Program.cs,
// with an explicit IHostEnvironment check as defense-in-depth.
[ApiController]
[Route("api/demo")]
[ApiExplorerSettings(IgnoreApi = true)]
public class DemoController : ControllerBase
{
    public const int MinTickets = 1;
    public const int MaxTickets = 500;
    public const int MaxScenarioNameLength = 100;

    private readonly StagePassDbContext _context;
    private readonly IHostEnvironment _env;

    public DemoController(StagePassDbContext context, IHostEnvironment env)
    {
        _context = context;
        _env = env;
    }

    // POST /api/demo/setup-scenario
    // Creates a demo performance with exactly {tickets} available tickets.
    // Returns the ticketTypeId and performanceId for the demo page to use.
    [HttpPost("setup-scenario")]
    public async Task<IActionResult> SetupScenario(
        [FromQuery] int tickets = 100,
        [FromQuery] string? scenarioName = null,
        CancellationToken ct = default)
    {
        if (!_env.IsDevelopment())
            return NotFound();

        if (tickets < MinTickets || tickets > MaxTickets)
        {
            return BadRequest(new
            {
                error = "InvalidTicketCount",
                message = $"tickets must be between {MinTickets} and {MaxTickets}."
            });
        }

        var rawScenarioName = Request.Query.TryGetValue("scenarioName", out var queryValues)
            ? queryValues.ToString()
            : (scenarioName ?? "Demo Scenario");

        var normalizedName = rawScenarioName.Trim();
        if (string.IsNullOrWhiteSpace(normalizedName) || normalizedName.Length > MaxScenarioNameLength)
        {
            return BadRequest(new
            {
                error = "InvalidScenarioName",
                message = $"scenarioName must be between 1 and {MaxScenarioNameLength} characters."
            });
        }

        // Find or create a Demo venue
        var venue = await _context.Venues.FirstOrDefaultAsync(v => v.Name == "Demo Venue", ct)
                    ?? new Venue { Name = "Demo Venue", Address = "1 Demo Street", City = "Testcity" };
        if (venue.VenueId == 0) _context.Venues.Add(venue);

        // Find or create a Demo organizer
        var org = await _context.Organizers.FirstOrDefaultAsync(o => o.Email == "demo@stagepass.local", ct)
                  ?? new Organizer { Name = "Demo Organizer", Email = "demo@stagepass.local" };
        if (org.OrganizerId == 0) _context.Organizers.Add(org);

        await _context.SaveChangesAsync(ct);

        // Create a fresh Event + Performance + TicketType for the scenario
        var evt = new Event
        {
            OrganizerId = org.OrganizerId,
            VenueId = venue.VenueId,
            Title = normalizedName,
            Category = "Demo",
            Description = $"Live concurrency demonstration: {tickets} tickets, multiple concurrent buyers."
        };
        _context.Events.Add(evt);
        await _context.SaveChangesAsync(ct);

        var perf = new Performance
        {
            EventId = evt.EventId,
            StartDateTime = DateTime.UtcNow.AddDays(1),
            Status = PerformanceStatus.Upcoming
        };
        _context.Performances.Add(perf);
        await _context.SaveChangesAsync(ct);

        var tier = new TicketType
        {
            PerformanceId = perf.PerformanceId,
            Name = "Demo Ticket",
            Price = 10.00m,
            Capacity = tickets,
            Available = tickets
        };
        _context.TicketTypes.Add(tier);
        await _context.SaveChangesAsync(ct);

        return Ok(new
        {
            ticketTypeId = tier.TicketTypeId,
            performanceId = perf.PerformanceId,
            capacity = tickets,
            scenarioName = normalizedName
        });
    }

    // GET /api/demo/snapshot/{ticketTypeId}
    // Returns the live inventory state for a given ticket type.
    [HttpGet("snapshot/{ticketTypeId:int}")]
    public async Task<IActionResult> GetSnapshot(int ticketTypeId, CancellationToken ct)
    {
        if (!_env.IsDevelopment())
            return NotFound();

        if (ticketTypeId <= 0)
        {
            return BadRequest(new
            {
                error = "InvalidTicketTypeId",
                message = "ticketTypeId must be a positive integer."
            });
        }

        var tt = await _context.TicketTypes.FindAsync([ticketTypeId], ct);
        if (tt == null) return NotFound();

        var pendingCount = await _context.ReservationHolds
            .Where(h => h.TicketTypeId == ticketTypeId && h.Status == ReservationStatus.Pending)
            .SumAsync(h => (int?)h.Quantity, ct) ?? 0;
        var confirmedCount = await _context.ReservationHolds
            .Where(h => h.TicketTypeId == ticketTypeId && h.Status == ReservationStatus.Confirmed)
            .SumAsync(h => (int?)h.Quantity, ct) ?? 0;

        return Ok(new
        {
            capacity       = tt.Capacity,
            available      = tt.Available,
            pendingHeld    = pendingCount,
            confirmed      = confirmedCount,
            invariantHolds = tt.Available + pendingCount + confirmedCount == tt.Capacity,
            formula        = $"{tt.Available} + {pendingCount} + {confirmedCount} = {tt.Available + pendingCount + confirmedCount} (Capacity: {tt.Capacity})"
        });
    }
}
