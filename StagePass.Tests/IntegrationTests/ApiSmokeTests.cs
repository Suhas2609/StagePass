using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StagePass.Api.Data;
using StagePass.Api.DTOs.Bookings;
using StagePass.Api.DTOs.Performances;
using StagePass.Api.DTOs.Reservations;

namespace StagePass.Tests.IntegrationTests;

/// <summary>HTTP-level smoke tests. Runs the real API (Testing environment) against StagePassTestDb.</summary>
[Collection("DatabaseIntegrationTests")]
public class ApiSmokeTests : IAsyncLifetime
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public ApiSmokeTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync() => await _factory.ResetDatabaseAsync();

    public Task DisposeAsync()
    {
        _client.Dispose();
        return Task.CompletedTask;
    }

    private async Task<int> FirstTicketTypeIdAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StagePassDbContext>();
        return (await db.TicketTypes.AsNoTracking().OrderBy(tt => tt.TicketTypeId).FirstAsync()).TicketTypeId;
    }

    private async Task<int> FirstPerformanceIdAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StagePassDbContext>();
        return (await db.Performances.AsNoTracking().OrderBy(p => p.PerformanceId).FirstAsync()).PerformanceId;
    }

    private static CreateReservationRequest NewRequest(int ticketTypeId, int quantity = 1, string customer = "api-user@example.com") => new()
    {
        TicketTypeId = ticketTypeId,
        Quantity = quantity,
        IdempotencyKey = Guid.NewGuid(),
        CustomerIdentifier = customer
    };

    private HttpRequestMessage OrganizerRequest(HttpMethod method, string url, bool withKey, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (withKey)
            request.Headers.Add("X-Api-Key", _factory.Services.GetRequiredService<IConfiguration>()["OrganizerApiKey"]);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return request;
    }

    [Fact]
    public async Task Test_GET_Performances_ReturnsUpcomingList()
    {
        var response = await _client.GetAsync("/api/performances");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = await response.Content.ReadFromJsonAsync<List<PerformanceListItemDto>>();
        Assert.NotNull(items);
        Assert.NotEmpty(items!);
        Assert.All(items, i => Assert.Equal("Upcoming", i.Status));
    }

    [Fact]
    public async Task Test_GET_Performances_PaginatesAndFilters()
    {
        var page1 = await (await _client.GetAsync("/api/performances?page=1&pageSize=2"))
            .Content.ReadFromJsonAsync<List<PerformanceListItemDto>>();
        var page2 = await (await _client.GetAsync("/api/performances?page=2&pageSize=2"))
            .Content.ReadFromJsonAsync<List<PerformanceListItemDto>>();
        var theatre = await (await _client.GetAsync("/api/performances?category=THEATRE"))
            .Content.ReadFromJsonAsync<List<PerformanceListItemDto>>();
        var none = await _client.GetAsync("/api/performances?category=no-such-category");
        var badDate = await _client.GetAsync("/api/performances?date=not-a-date");

        Assert.Equal(2, page1!.Count);
        Assert.Single(page2!);
        Assert.Empty(page1.Select(p => p.PerformanceId).Intersect(page2!.Select(p => p.PerformanceId)));
        Assert.NotEmpty(theatre!);
        Assert.All(theatre!, p => Assert.Equal("Theatre", p.Category));
        Assert.Equal(HttpStatusCode.OK, none.StatusCode);
        Assert.Equal("[]", (await none.Content.ReadAsStringAsync()).Trim());
        Assert.Equal(HttpStatusCode.BadRequest, badDate.StatusCode);
    }

    [Fact]
    public async Task Test_GET_Performance_ValidId_Returns200WithoutCapacity()
    {
        var id = await FirstPerformanceIdAsync();

        var response = await _client.GetAsync($"/api/performances/{id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var details = JsonSerializer.Deserialize<PerformanceDetailsDto>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotEmpty(details!.TicketTypes);
        Assert.DoesNotContain("capacity", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Test_GET_Performance_InvalidId_Returns404()
    {
        var response = await _client.GetAsync("/api/performances/99999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Test_POST_Reservation_ValidRequest_Returns201()
    {
        var ticketTypeId = await FirstTicketTypeIdAsync();

        var response = await _client.PostAsJsonAsync("/api/reservations", NewRequest(ticketTypeId, 2));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        var hold = await response.Content.ReadFromJsonAsync<ReservationResponse>();
        Assert.True(hold!.HoldId > 0);
        Assert.True(hold.ExpiresAt > DateTime.UtcNow);
        Assert.Equal("Pending", hold.Status);
        Assert.False(string.IsNullOrEmpty(hold.TicketTypeName));
    }

    [Fact]
    public async Task Test_POST_Reservation_IdempotentRetry_Returns200WithSameHold()
    {
        var ticketTypeId = await FirstTicketTypeIdAsync();
        var request = NewRequest(ticketTypeId);

        var first = await _client.PostAsJsonAsync("/api/reservations", request);
        var second = await _client.PostAsJsonAsync("/api/reservations", request);
        var reused = await _client.PostAsJsonAsync("/api/reservations", NewRequest(ticketTypeId, 3));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(
            (await first.Content.ReadFromJsonAsync<ReservationResponse>())!.HoldId,
            (await second.Content.ReadFromJsonAsync<ReservationResponse>())!.HoldId);
        Assert.Equal(HttpStatusCode.Created, reused.StatusCode); // different key => new hold

        // Same key, different quantity => 409 IdempotencyKeyReused
        var conflicting = new CreateReservationRequest
        {
            TicketTypeId = request.TicketTypeId,
            Quantity = request.Quantity + 1,
            IdempotencyKey = request.IdempotencyKey,
            CustomerIdentifier = request.CustomerIdentifier
        };
        var conflict = await _client.PostAsJsonAsync("/api/reservations", conflicting);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Contains("IdempotencyKeyReused", await conflict.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Test_POST_Reservation_InsufficientInventory_Returns409()
    {
        var ticketTypeId = await FirstTicketTypeIdAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StagePassDbContext>();
            await db.Database.ExecuteSqlRawAsync("UPDATE TicketTypes SET Available = 0 WHERE TicketTypeId = {0}", ticketTypeId);
        }

        var response = await _client.PostAsJsonAsync("/api/reservations", NewRequest(ticketTypeId));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("InsufficientInventory", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Test_POST_Reservation_InvalidBody_Returns400()
    {
        var ticketTypeId = await FirstTicketTypeIdAsync();

        var zeroQuantity = await _client.PostAsJsonAsync("/api/reservations", NewRequest(ticketTypeId, 0));
        var emptyKey = await _client.PostAsJsonAsync("/api/reservations", new
        {
            ticketTypeId,
            quantity = 1,
            idempotencyKey = Guid.Empty,
            customerIdentifier = "x@example.com"
        });

        Assert.Equal(HttpStatusCode.BadRequest, zeroQuantity.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, emptyKey.StatusCode);
    }

    [Fact]
    public async Task Test_GET_Reservation_UnknownHold_Returns404()
    {
        var response = await _client.GetAsync("/api/reservations/99999999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Test_Confirm_And_GetBooking_EndToEnd()
    {
        var ticketTypeId = await FirstTicketTypeIdAsync();

        var create = await _client.PostAsJsonAsync("/api/reservations", NewRequest(ticketTypeId, 2, "e2e@example.com"));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var hold = await create.Content.ReadFromJsonAsync<ReservationResponse>();

        var getHold = await _client.GetAsync($"/api/reservations/{hold!.HoldId}");
        Assert.Equal(HttpStatusCode.OK, getHold.StatusCode);

        var confirm = await _client.PostAsJsonAsync($"/api/reservations/{hold.HoldId}/confirm",
            new ConfirmReservationRequest { CustomerIdentifier = "e2e@example.com" });
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        var reference = (await confirm.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("bookingReference").GetString();
        Assert.StartsWith("SP-", reference);

        var booking = await _client.GetAsync($"/api/bookings/{reference}");
        Assert.Equal(HttpStatusCode.OK, booking.StatusCode);
        var details = await booking.Content.ReadFromJsonAsync<BookingDetailsDto>();
        Assert.Equal(reference, details!.BookingReference);
        Assert.Equal(2, details.Quantity);
        Assert.Equal("Confirmed", details.Status);

        // Second confirm => 409 AlreadyConfirmed; wrong owner is checked on a fresh hold below.
        var again = await _client.PostAsJsonAsync($"/api/reservations/{hold.HoldId}/confirm",
            new ConfirmReservationRequest { CustomerIdentifier = "e2e@example.com" });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Contains("AlreadyConfirmed", await again.Content.ReadAsStringAsync());

        var missing = await _client.GetAsync("/api/bookings/SP-DOESNOTEXIST");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Test_Confirm_UnknownHold_Returns404_And_WrongOwner_Returns409()
    {
        var ticketTypeId = await FirstTicketTypeIdAsync();

        var unknown = await _client.PostAsJsonAsync("/api/reservations/99999999/confirm",
            new ConfirmReservationRequest { CustomerIdentifier = "x@example.com" });
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        var create = await _client.PostAsJsonAsync("/api/reservations", NewRequest(ticketTypeId, 1, "owner@example.com"));
        var hold = await create.Content.ReadFromJsonAsync<ReservationResponse>();
        var wrongOwner = await _client.PostAsJsonAsync($"/api/reservations/{hold!.HoldId}/confirm",
            new ConfirmReservationRequest { CustomerIdentifier = "intruder@example.com" });
        Assert.Equal(HttpStatusCode.Conflict, wrongOwner.StatusCode);
        Assert.Contains("InvalidOwner", await wrongOwner.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Test_OrganizerEndpoint_WithoutApiKey_Returns401()
    {
        var performanceId = await FirstPerformanceIdAsync();

        var list = await _client.SendAsync(OrganizerRequest(HttpMethod.Get, $"/api/organizer/performances/{performanceId}/bookings", withKey: false));
        var wrongKey = new HttpRequestMessage(HttpMethod.Get, $"/api/organizer/performances/{performanceId}/bookings");
        wrongKey.Headers.Add("X-Api-Key", "definitely-wrong");
        var wrong = await _client.SendAsync(wrongKey);
        var create = await _client.SendAsync(OrganizerRequest(HttpMethod.Post, "/api/organizer/events", withKey: false,
            new { title = "t", category = "c", venueId = 1, organizerId = 1 }));
        var cancel = await _client.SendAsync(OrganizerRequest(HttpMethod.Patch, $"/api/organizer/performances/{performanceId}/cancel", withKey: false));

        Assert.Equal(HttpStatusCode.Unauthorized, list.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, create.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, cancel.StatusCode);
    }

    [Fact]
    public async Task Test_OrganizerEndpoint_WithApiKey_Returns200()
    {
        var performanceId = await FirstPerformanceIdAsync();

        var response = await _client.SendAsync(OrganizerRequest(HttpMethod.Get, $"/api/organizer/performances/{performanceId}/bookings", withKey: true));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Array, body.GetProperty("bookings").ValueKind);
        Assert.NotEmpty(body.GetProperty("summary").EnumerateArray());
    }

    [Fact]
    public async Task Test_Organizer_CreateEventPerformanceTicketType_ThenCancel()
    {
        int eventId = 0;
        try
        {
            int venueId, organizerId;
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<StagePassDbContext>();
                venueId = (await db.Venues.AsNoTracking().FirstAsync()).VenueId;
                organizerId = (await db.Organizers.AsNoTracking().FirstAsync()).OrganizerId;
            }

            var ev = await _client.SendAsync(OrganizerRequest(HttpMethod.Post, "/api/organizer/events", true,
                new { title = "Api Test Event", category = "Test", description = "d", venueId, organizerId }));
            Assert.Equal(HttpStatusCode.Created, ev.StatusCode);
            eventId = (await ev.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("eventId").GetInt32();

            var past = await _client.SendAsync(OrganizerRequest(HttpMethod.Post, "/api/organizer/performances", true,
                new { eventId, startDateTime = DateTime.UtcNow.AddDays(-1) }));
            Assert.Equal(HttpStatusCode.BadRequest, past.StatusCode);

            var perf = await _client.SendAsync(OrganizerRequest(HttpMethod.Post, "/api/organizer/performances", true,
                new { eventId, startDateTime = DateTime.UtcNow.AddDays(10) }));
            Assert.Equal(HttpStatusCode.Created, perf.StatusCode);
            var performanceId = (await perf.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("performanceId").GetInt32();

            var tt = await _client.SendAsync(OrganizerRequest(HttpMethod.Post, $"/api/organizer/performances/{performanceId}/tickettypes", true,
                new { name = "GA", price = 12.5, capacity = 30, available = 5 }));
            Assert.Equal(HttpStatusCode.Created, tt.StatusCode);
            var ticketTypeId = (await tt.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("ticketTypeId").GetInt32();

            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<StagePassDbContext>();
                var created = await db.TicketTypes.AsNoTracking().FirstAsync(x => x.TicketTypeId == ticketTypeId);
                Assert.Equal(30, created.Available); // Available comes from Capacity, never from the body
            }

            var duplicate = await _client.SendAsync(OrganizerRequest(HttpMethod.Post, $"/api/organizer/performances/{performanceId}/tickettypes", true,
                new { name = "GA", price = 12.5, capacity = 30 }));
            Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

            var cancel = await _client.SendAsync(OrganizerRequest(HttpMethod.Patch, $"/api/organizer/performances/{performanceId}/cancel", true));
            Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
            var cancelAgain = await _client.SendAsync(OrganizerRequest(HttpMethod.Patch, $"/api/organizer/performances/{performanceId}/cancel", true));
            Assert.Equal(HttpStatusCode.Conflict, cancelAgain.StatusCode);

            var details = await _client.GetAsync($"/api/performances/{performanceId}");
            Assert.Equal(HttpStatusCode.NotFound, details.StatusCode); // cancelled => not Upcoming
        }
        finally
        {
            // Remove only the rows created by this test (Event -> Performances -> TicketTypes cascade),
            // so the seed-count test still sees exactly the seeded data.
            if (eventId != 0)
            {
                using var scope = _factory.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<StagePassDbContext>();
                await db.Database.ExecuteSqlRawAsync("DELETE FROM Events WHERE EventId = {0}", eventId);
            }
        }
    }
}
