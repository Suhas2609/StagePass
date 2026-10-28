using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using StagePass.Api.BackgroundServices;
using StagePass.Api.Controllers;
using StagePass.Api.Data;
using StagePass.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// 1. Controllers
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Exclude DemoController from route discovery and registration unless running in Development.
builder.Services.AddOptions<MvcOptions>()
    .Configure<IHostEnvironment>((options, env) =>
    {
        if (!env.IsDevelopment())
        {
            options.Conventions.Add(new ExcludeControllerConvention(typeof(DemoController)));
        }
    });

// 2. DbContext
builder.Services.AddDbContext<StagePassDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// 3. Services
builder.Services.AddScoped<InventoryService>();
builder.Services.AddScoped<ReservationService>();
builder.Services.AddScoped<BookingService>();

// 4. Background worker
builder.Services.AddSingleton<HoldExpiryWorker>();
builder.Services.AddHostedService<HoldExpiryWorker>();

// 5. Rate limiting: policy "booking" (10 requests per client IP per 60 seconds), applied only to
// POST /api/reservations via [EnableRateLimiting("booking")]. Enabled only when
// RateLimiting:BookingEnabled parses to boolean true; otherwise the policy is a no-op (e.g. Testing).
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("booking", httpContext =>
    {
        var config = httpContext.RequestServices.GetRequiredService<IConfiguration>();
        var bookingRateLimitEnabled =
            bool.TryParse(config["RateLimiting:BookingEnabled"], out var enabled) && enabled;

        if (!bookingRateLimitEnabled)
            return RateLimitPartition.GetNoLimiter("booking-disabled");

        return RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromSeconds(60),
                QueueLimit = 0
            });
    });
});
var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    var connectionString = app.Configuration.GetConnectionString("DefaultConnection");
    if (!string.IsNullOrWhiteSpace(connectionString) && !connectionString.StartsWith("REPLACE_WITH_"))
    {
        var csBuilder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connectionString);
        var dbName = csBuilder.InitialCatalog;
        var systemDbs = new[] { "master", "model", "msdb", "tempdb" };

        if (!string.IsNullOrWhiteSpace(dbName) && !systemDbs.Contains(dbName, StringComparer.OrdinalIgnoreCase))
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<StagePassDbContext>();
            await db.Database.MigrateAsync();
            await DbInitializer.SeedAsync(app.Services);
        }
    }
}

app.UseHttpsRedirection();
// 6. Static files
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthorization();
app.MapControllers();

app.Run();

public partial class Program { }

internal sealed class ExcludeControllerConvention(Type controllerType) : IApplicationModelConvention
{
    public void Apply(ApplicationModel application)
    {
        for (var i = application.Controllers.Count - 1; i >= 0; i--)
        {
            if (application.Controllers[i].ControllerType.AsType() == controllerType)
            {
                application.Controllers.RemoveAt(i);
            }
        }
    }
}
