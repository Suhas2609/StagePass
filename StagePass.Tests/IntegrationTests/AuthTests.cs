using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StagePass.Api.Data;

namespace StagePass.Tests.IntegrationTests;

/// <summary>Authentication endpoint tests. Runs the real API (Testing environment) against StagePassTestDb.</summary>
[Collection("DatabaseIntegrationTests")]
public class AuthTests : IAsyncLifetime
{
    private const string Password = "Passw0rd!123";

    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AuthTests(TestWebApplicationFactory factory)
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

    private static string NewEmail() => $"auth-{Guid.NewGuid():N}@example.com";

    private Task<HttpResponseMessage> RegisterAsync(string email, string password = Password, string name = "Auth Tester", bool isOrganizer = false) =>
        _client.PostAsJsonAsync("/api/auth/register", new { email, password, name, isOrganizer });

    private Task<HttpResponseMessage> LoginAsync(string email, string password = Password) =>
        _client.PostAsJsonAsync("/api/auth/login", new { email, password });

    private async Task<HttpResponseMessage> GetAsync(string url, string? token)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (token is not null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _client.SendAsync(req);
    }

    private async Task<HttpResponseMessage> PostAsync(string url, string? token)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        if (token is not null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _client.SendAsync(req);
    }

    [Fact]
    public async Task Post_Register_WithValidData_Returns200WithToken()
    {
        var email = NewEmail();

        var resp = await RegisterAsync(email, name: "  Alice  ");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(Guid.TryParse(body.GetProperty("token").GetString(), out _));
        Assert.Equal("Alice", body.GetProperty("name").GetString());
        Assert.Equal(email, body.GetProperty("email").GetString());
        Assert.False(body.GetProperty("isOrganizer").GetBoolean());
        Assert.True(body.GetProperty("expiresAt").GetDateTime() > DateTime.UtcNow.AddDays(6));
    }

    [Fact]
    public async Task Post_Register_StoresHashedPassword_NeverPlaintext_AndNeverReturnsIt()
    {
        var email = NewEmail();

        var resp = await RegisterAsync(email);
        var raw = await resp.Content.ReadAsStringAsync();

        Assert.DoesNotContain(Password, raw);
        Assert.DoesNotContain("passwordHash", raw, StringComparison.OrdinalIgnoreCase);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StagePassDbContext>();
        var user = await db.AppUsers.AsNoTracking().SingleAsync(u => u.Email == email);
        Assert.NotEqual(Password, user.PasswordHash);
        Assert.NotEmpty(user.PasswordSalt);
    }

    [Fact]
    public async Task Post_Register_DuplicateEmail_Returns409()
    {
        var email = NewEmail();
        Assert.Equal(HttpStatusCode.OK, (await RegisterAsync(email)).StatusCode);

        var second = await RegisterAsync(email.ToUpperInvariant()); // email is case-insensitive

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var body = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("EmailAlreadyRegistered", body.GetProperty("error").GetString());
    }

    [Theory]
    [InlineData("not-an-email", "Passw0rd!123", "Name")]
    [InlineData("a@example.com", "short", "Name")]
    [InlineData("a@example.com", "Passw0rd!123", "")]
    public async Task Post_Register_InvalidInput_Returns400(string email, string password, string name)
    {
        var resp = await RegisterAsync(email, password, name);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Post_Login_WithValidCredentials_Returns200WithToken()
    {
        var email = NewEmail();
        await RegisterAsync(email);

        var resp = await LoginAsync(email.ToUpperInvariant());

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(Guid.TryParse(body.GetProperty("token").GetString(), out _));
    }

    [Fact]
    public async Task Post_Login_WithInvalidPassword_Returns401()
    {
        var email = NewEmail();
        await RegisterAsync(email);

        var resp = await LoginAsync(email, "WrongPassword!1");

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("InvalidCredentials", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Post_Login_UnknownEmail_Returns401_SameErrorAsWrongPassword()
    {
        var resp = await LoginAsync(NewEmail());

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("InvalidCredentials", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Post_Login_SeededDemoOrganizer_Succeeds_AsOrganizer()
    {
        var resp = await LoginAsync(TestWebApplicationFactory.OrganizerEmail, TestWebApplicationFactory.OrganizerPassword);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("isOrganizer").GetBoolean());
    }

    [Fact]
    public async Task Get_Me_WithValidToken_Returns200WithUserInfo()
    {
        var email = NewEmail();
        await RegisterAsync(email, name: "Bob");
        var login = await (await LoginAsync(email)).Content.ReadFromJsonAsync<JsonElement>();
        var token = login.GetProperty("token").GetString();

        var resp = await GetAsync("/api/auth/me", token);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(email, body.GetProperty("email").GetString());
        Assert.Equal("Bob", body.GetProperty("name").GetString());
        Assert.False(body.GetProperty("isOrganizer").GetBoolean());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("00000000-0000-0000-0000-000000000001")]
    [InlineData("garbage")]
    public async Task Get_Me_WithMissingOrInvalidToken_Returns401(string? token)
    {
        var resp = await GetAsync("/api/auth/me", token);

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Post_Logout_InvalidatesSession()
    {
        var email = NewEmail();
        await RegisterAsync(email);
        var login = await (await LoginAsync(email)).Content.ReadFromJsonAsync<JsonElement>();
        var token = login.GetProperty("token").GetString();
        Assert.Equal(HttpStatusCode.OK, (await GetAsync("/api/auth/me", token)).StatusCode);

        var logout = await PostAsync("/api/auth/logout", token);
        Assert.Equal(HttpStatusCode.OK, logout.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await GetAsync("/api/auth/me", token)).StatusCode);
    }

    [Fact]
    public async Task Post_Logout_OnlyInvalidatesTheCurrentSession()
    {
        var email = NewEmail();
        await RegisterAsync(email);
        var first = (await (await LoginAsync(email)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
        var second = (await (await LoginAsync(email)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
        Assert.NotEqual(first, second);

        await PostAsync("/api/auth/logout", first);

        Assert.Equal(HttpStatusCode.Unauthorized, (await GetAsync("/api/auth/me", first)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await GetAsync("/api/auth/me", second)).StatusCode);
    }

    [Fact]
    public async Task Post_Logout_WithoutToken_Returns401()
    {
        var resp = await PostAsync("/api/auth/logout", token: null);

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Organizer_Endpoint_WithoutToken_Returns401()
    {
        var resp = await GetAsync("/api/organizer/performances/1/bookings", token: null);

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Organizer_Endpoint_WithNonOrganizerToken_Returns403()
    {
        var (token, _) = await _factory.RegisterUserAsync(isOrganizer: false);

        var resp = await GetAsync("/api/organizer/performances/1/bookings", token);

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Organizer_Endpoint_WithSelfRegisteredOrganizerWithoutOrganizerRecord_CannotCreateEvents()
    {
        // IsOrganizer=true alone is not enough to create events: there must be an Organizer record with the same email.
        var (token, _) = await _factory.RegisterUserAsync(isOrganizer: true);
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/organizer/events")
        {
            Content = JsonContent.Create(new { title = "t", category = "c", venueId = 1, organizerId = 1 })
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resp = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("OrganizerRecordNotFound", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Public_Endpoints_StillWorkWithoutAuthentication()
    {
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/performances")).StatusCode);
    }

    [Fact]
    public async Task Get_OrganizerVenues_WithOrganizerToken_ReturnsOk()
    {
        var token = await _factory.LoginAsOrganizerAsync();
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/organizer/venues");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Get_OrganizerVenues_WithoutToken_Returns401()
    {
        var response = await _client.GetAsync("/api/organizer/venues");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
