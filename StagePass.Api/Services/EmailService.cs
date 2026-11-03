using System.Net;
using System.Net.Mail;

namespace StagePass.Api.Services;

public interface IEmailService
{
    Task SendBookingConfirmationAsync(BookingConfirmationEmail email, CancellationToken ct);
}

public sealed record BookingConfirmationEmail(
    string ToAddress,
    string BookingReference,
    string EventTitle,
    string PerformanceDateTime,
    string VenueName,
    string TicketTypeName,
    int    Quantity,
    decimal TotalPrice
);

public sealed class EmailService(IConfiguration config, ILogger<EmailService> logger)
    : IEmailService
{
    public async Task SendBookingConfirmationAsync(
        BookingConfirmationEmail email, CancellationToken ct)
    {
        var enabled = config.GetValue<bool>("Email:Enabled");
        if (!enabled)
        {
            logger.LogInformation(
                "Email disabled. Would have sent booking confirmation to {To}.", email.ToAddress);
            return;
        }

        var host     = config["Email:SmtpHost"]    ?? "smtp.gmail.com";
        var port     = config.GetValue<int>("Email:SmtpPort", 587);
        var from     = config["Email:From"]        ?? "";
        var password = config["Email:Password"]    ?? "";
        var display  = config["Email:DisplayName"] ?? "StagePass";

        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning(
                "Email:Enabled is true but Email:From or Email:Password is not configured.");
            return;
        }

        try
        {
            using var msg = new MailMessage
            {
                From       = new MailAddress(from, display),
                Subject    = $"Booking Confirmed — {email.BookingReference} | StagePass",
                Body       = BuildHtmlBody(email),
                IsBodyHtml = true,
            };
            msg.To.Add(email.ToAddress);

            using var client = new SmtpClient(host, port)
            {
                EnableSsl   = true,
                Credentials = new NetworkCredential(from, password),
                Timeout     = 10_000,
            };

            await client.SendMailAsync(msg, ct);
            logger.LogInformation(
                "Booking confirmation sent to {To} for {Ref}.",
                email.ToAddress, email.BookingReference);
        }
        catch (Exception ex)
        {
            // Email failure must never cause the booking to fail.
            logger.LogError(ex,
                "Failed to send booking confirmation to {To} for {Ref}. " +
                "The booking itself is still confirmed.",
                email.ToAddress, email.BookingReference);
        }
    }

    private static string BuildHtmlBody(BookingConfirmationEmail e) => $"""
        <!DOCTYPE html>
        <html lang="en">
        <head>
          <meta charset="UTF-8">
          <meta name="viewport" content="width=device-width,initial-scale=1">
          <title>Booking Confirmed</title>
        </head>
        <body style="margin:0;padding:0;background:#0c0c12;font-family:system-ui,-apple-system,sans-serif;">
          <div style="max-width:560px;margin:0 auto;padding:32px 24px;">

            <!-- Header -->
            <div style="text-align:center;margin-bottom:32px;">
              <h1 style="font-size:1.4rem;font-weight:900;color:#ffffff;letter-spacing:-0.03em;margin:0;">
                Stage<span style="color:#a78bfa">Pass</span>
              </h1>
            </div>

            <!-- Success card -->
            <div style="background:#12121a;border:1px solid rgba(255,255,255,0.09);
                        border-radius:16px;overflow:hidden;margin-bottom:24px;">
              <div style="background:linear-gradient(135deg,#0a0a18,#1a0850 50%,#2a0c6a);
                          padding:32px 32px 24px;text-align:center;">
                <div style="width:56px;height:56px;border-radius:50%;background:rgba(16,185,129,0.15);
                            border:2px solid #10b981;display:inline-flex;align-items:center;
                            justify-content:center;font-size:1.5rem;margin-bottom:16px;">✓</div>
                <h2 style="font-size:1.4rem;font-weight:800;color:#ffffff;
                           letter-spacing:-0.03em;margin:0 0 6px;">Booking confirmed!</h2>
                <p style="color:rgba(255,255,255,0.6);font-size:0.9rem;margin:0;">
                  Present this reference at the venue entrance.
                </p>
              </div>

              <!-- Reference -->
              <div style="padding:24px 32px;text-align:center;
                          border-bottom:1px solid rgba(255,255,255,0.06);">
                <p style="font-size:0.7rem;text-transform:uppercase;letter-spacing:0.1em;
                          color:rgba(160,160,184,0.7);font-weight:700;margin:0 0 8px;">
                  Booking Reference
                </p>
                <p style="font-family:'Courier New',monospace;font-size:2rem;font-weight:900;
                          color:#a78bfa;letter-spacing:0.06em;margin:0;">
                  {e.BookingReference}
                </p>
              </div>

              <!-- Details -->
              <div style="padding:8px 32px 16px;">
                {DetailRow("Event",       e.EventTitle)}
                {DetailRow("Date & Time", e.PerformanceDateTime)}
                {DetailRow("Venue",       e.VenueName)}
                {DetailRow("Ticket Type", e.TicketTypeName)}
                {DetailRow("Quantity",    e.Quantity.ToString())}
                {DetailRow("Total Paid",  $"£{e.TotalPrice:F2}")}
              </div>
            </div>

            <!-- Footer -->
            <div style="text-align:center;">
              <p style="font-size:0.75rem;color:rgba(96,96,120,0.9);margin:0 0 8px;">
                Questions? Reply to this email or visit
                <a href="https://stagepass.local"
                   style="color:#a78bfa;text-decoration:none;">stagepass.local</a>
              </p>
              <p style="font-size:0.7rem;color:rgba(58,58,78,0.9);margin:0;">
                © 2026 StagePass · This is an automated confirmation.
              </p>
            </div>

          </div>
        </body>
        </html>
        """;

    private static string DetailRow(string label, string value) => $"""
        <div style="display:flex;justify-content:space-between;align-items:center;
                    padding:12px 0;border-bottom:1px solid rgba(255,255,255,0.05);">
          <span style="font-size:0.82rem;color:rgba(160,160,184,0.8);">{label}</span>
          <span style="font-size:0.82rem;font-weight:600;color:#eeeef5;
                       text-align:right;max-width:65%;">{value}</span>
        </div>
        """;
}
