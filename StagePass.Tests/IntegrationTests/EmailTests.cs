using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using StagePass.Api.Entities;
using Xunit;

namespace StagePass.Tests.IntegrationTests;

[Collection("Integration")]
public class EmailTests : IClassFixture<StagePassTestFixture>
{
    private readonly HttpClient _client;
    private readonly StagePassTestFixture _fixture;

    public EmailTests(StagePassTestFixture fixture)
    {
        _fixture = fixture;
        _client  = fixture.CreateClient();
        fixture.ResetDatabaseAsync().Wait();
    }

    [Fact]
    public async Task ConfirmBooking_WithEmailDisabled_StillReturns200()
    {
        // Email is disabled in Testing environment (Email:Enabled = false / absent)
        // The booking should complete successfully regardless
        var seedTt = await _fixture.GetSeedTicketTypeAsync();
        var holdRes = await _client.PostAsJsonAsync("/api/reservations", new
        {
            ticketTypeId       = seedTt.TicketTypeId,
            quantity           = 1,
            idempotencyKey     = Guid.NewGuid(),
            customerIdentifier = "emailtest@example.com",
        });
        holdRes.EnsureSuccessStatusCode();
        var hold = await holdRes.Content.ReadFromJsonAsync<JsonElement>();
        var holdId = hold.GetProperty("holdId").GetInt32();

        var confirmRes = await _client.PostAsJsonAsync(
            $"/api/reservations/{holdId}/confirm",
            new { customerIdentifier = "emailtest@example.com" });

        Assert.Equal(HttpStatusCode.OK, confirmRes.StatusCode);
        var body = await confirmRes.Content.ReadFromJsonAsync<JsonElement>();
        Assert.StartsWith("SP-", body.GetProperty("bookingReference").GetString()!);
    }

    [Fact]
    public async Task ConfirmBooking_CustomerWithoutAt_SkipsEmail_StillConfirms()
    {
        // CustomerIdentifier without @ (e.g. session token style)
        // EmailService should skip sending (Contains('@') check)
        var seedTt = await _fixture.GetSeedTicketTypeAsync();
        var holdRes = await _client.PostAsJsonAsync("/api/reservations", new
        {
            ticketTypeId       = seedTt.TicketTypeId,
            quantity           = 1,
            idempotencyKey     = Guid.NewGuid(),
            customerIdentifier = "session-token-no-at-sign",
        });
        holdRes.EnsureSuccessStatusCode();
        var hold = await holdRes.Content.ReadFromJsonAsync<JsonElement>();
        var holdId = hold.GetProperty("holdId").GetInt32();

        var confirmRes = await _client.PostAsJsonAsync(
            $"/api/reservations/{holdId}/confirm",
            new { customerIdentifier = "session-token-no-at-sign" });

        Assert.Equal(HttpStatusCode.OK, confirmRes.StatusCode);
    }
}
