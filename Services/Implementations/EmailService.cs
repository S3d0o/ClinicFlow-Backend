using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using Shared.Settings;

namespace Services.Implementations;

public class EmailService(
    IOptions<EmailSettings> emailOptions,
    ILogger<EmailService> logger) : IEmailService
{
    private readonly EmailSettings _settings = emailOptions.Value;

    // ── SendEmailConfirmationAsync ─────────────────────────────────────────
    // Called after registration.
    // Token is passed raw — browser encoding + frontend decodeURIComponent
    // ensure the value reaching the backend matches what Identity stored.
    public async Task SendEmailConfirmationAsync(
        string email, Guid userId, string token)
    {
        // Raw token in URL — do NOT Uri.EscapeDataString here.
        // The browser will encode it naturally; ConfirmEmailPage.tsx calls
        // decodeURIComponent before posting to /auth/confirm-email.
        var confirmUrl = $"http://localhost:3000/#/confirm-email?userId={userId}&token={token}";

        var subject = "Confirm your ClinicFlow account";
        var body = BuildEmailTemplate(
            previewText: "One click to verify your ClinicFlow account.",
            headerTitle: "Verify Your Email Address",
            bodyHtml: $"""
                <p style="margin:0 0 20px;font-size:15px;line-height:1.6;color:#374151;">
                    Thank you for creating a <strong>ClinicFlow</strong> account.
                    To complete your registration and access all features, please
                    verify your email address by clicking the button below.
                </p>
                <p style="margin:0 0 32px;font-size:14px;color:#6B7280;">
                    This link will expire in <strong>24 hours</strong>.
                </p>
                {PrimaryButton("Confirm Email Address", confirmUrl)}
                <p style="margin:32px 0 0;font-size:13px;color:#9CA3AF;text-align:center;">
                    If you did not create a ClinicFlow account, you can safely ignore this email.
                </p>
            """);

        await SendAsync(email, "ClinicFlow User", subject, body);
        logger.LogInformation("Confirmation email sent to {Email}", email);
    }

    // ── SendPasswordResetAsync ────────────────────────────────────────────
    // Called from ForgotPasswordAsync.
    // Token is passed raw — same encoding contract as above.
    public async Task SendPasswordResetAsync(
        string email, Guid userId, string token)
    {
        var resetUrl = $"http://localhost:3000/#/reset-password?userId={userId}&token={token}";

        var subject = "Reset your ClinicFlow password";
        var body = BuildEmailTemplate(
            previewText: "A password reset was requested for your ClinicFlow account.",
            headerTitle: "Password Reset Request",
            bodyHtml: $"""
                <p style="margin:0 0 20px;font-size:15px;line-height:1.6;color:#374151;">
                    We received a request to reset the password associated with your
                    <strong>ClinicFlow</strong> account. Click the button below to
                    choose a new password.
                </p>
                <p style="margin:0 0 32px;font-size:14px;color:#6B7280;">
                    This link will expire in <strong>1 hour</strong>. If you did not
                    request a password reset, no action is required — your account
                    remains secure.
                </p>
                {PrimaryButton("Reset My Password", resetUrl)}
                <p style="margin:32px 0 0;font-size:13px;color:#9CA3AF;text-align:center;">
                    For security, this link can only be used once.
                </p>
            """);

        await SendAsync(email, "ClinicFlow User", subject, body);
        logger.LogInformation("Password reset email sent to {Email}", email);
    }

    // ── SendAppointmentConfirmationAsync ──────────────────────────────────
    // Called from AppointmentService.BookAppointmentAsync after booking.
    public async Task SendAppointmentConfirmationAsync(
        string email, string patientName,
        string doctorName, DateOnly date, TimeOnly time)
    {
        var subject = "Appointment Confirmed — ClinicFlow";
        var body = BuildEmailTemplate(
            previewText: $"Your appointment with Dr. {doctorName} is confirmed.",
            headerTitle: "Appointment Confirmed",
            bodyHtml: $"""
                <p style="margin:0 0 24px;font-size:15px;line-height:1.6;color:#374151;">
                    Dear <strong>{patientName}</strong>,<br/>
                    Your appointment has been successfully booked. Here are your details:
                </p>
                {AppointmentTable(doctorName, date, time)}
                <p style="margin:24px 0 0;font-size:14px;line-height:1.6;color:#6B7280;">
                    Please arrive <strong>10 minutes early</strong> to complete any
                    necessary paperwork. If you need to cancel or reschedule, please
                    do so at least 24 hours in advance through your ClinicFlow account.
                </p>
            """);

        await SendAsync(email, patientName, subject, body);
        logger.LogInformation("Appointment confirmation email sent to {Email}", email);
    }

    // ── SendAppointmentReminderAsync ──────────────────────────────────────
    // Called from the background job for appointments starting in 24 h.
    public async Task SendAppointmentReminderAsync(
        string email, string patientName,
        string doctorName, DateOnly date, TimeOnly time)
    {
        var subject = "Appointment Reminder — ClinicFlow";
        var body = BuildEmailTemplate(
            previewText: $"Reminder: your appointment with Dr. {doctorName} is tomorrow.",
            headerTitle: "Upcoming Appointment Reminder",
            bodyHtml: $"""
                <p style="margin:0 0 24px;font-size:15px;line-height:1.6;color:#374151;">
                    Dear <strong>{patientName}</strong>,<br/>
                    This is a friendly reminder that you have an appointment scheduled
                    for <strong>tomorrow</strong>. Here are your details:
                </p>
                {AppointmentTable(doctorName, date, time)}
                <p style="margin:24px 0 0;font-size:14px;line-height:1.6;color:#6B7280;">
                    Please arrive <strong>10 minutes early</strong>. To manage your
                    appointments, visit your ClinicFlow account.
                </p>
            """);

        await SendAsync(email, patientName, subject, body);
        logger.LogInformation("Appointment reminder email sent to {Email}", email);
    }

    // ── Private helpers ───────────────────────────────────────────────────

    /// <summary>
    /// Wraps any email body content in the shared ClinicFlow HTML shell.
    /// All four email types call this — change the design here once and
    /// it propagates everywhere.
    /// </summary>
    private static string BuildEmailTemplate(
        string previewText, string headerTitle, string bodyHtml) => $"""
        <!DOCTYPE html>
        <html lang="en">
        <head>
          <meta charset="UTF-8" />
          <meta name="viewport" content="width=device-width, initial-scale=1.0" />
          <meta http-equiv="X-UA-Compatible" content="IE=edge" />
          <title>{headerTitle}</title>
        </head>
        <body style="margin:0;padding:0;background-color:#F3F4F6;font-family:'Segoe UI',Arial,sans-serif;">

          <!-- Preview text (hidden, shown in inbox snippet) -->
          <span style="display:none;max-height:0;overflow:hidden;mso-hide:all;">
            {previewText}
          </span>

          <!-- Outer wrapper -->
          <table width="100%" cellpadding="0" cellspacing="0" border="0"
                 style="background:#F3F4F6;padding:40px 16px;">
            <tr>
              <td align="center">
                <table width="600" cellpadding="0" cellspacing="0" border="0"
                       style="max-width:600px;width:100%;">

                  <!-- ── Header ── -->
                  <tr>
                    <td align="center"
                        style="background:linear-gradient(135deg,#0D9488,#0F766E);
                               border-radius:12px 12px 0 0;padding:36px 40px;">
                      <!-- Logo mark -->
                      <table cellpadding="0" cellspacing="0" border="0">
                        <tr>
                          <td align="center"
                              style="background:rgba(255,255,255,0.15);
                                     border-radius:12px;padding:10px 14px;
                                     display:inline-block;">
                            <span style="font-size:22px;font-weight:700;
                                         color:#ffffff;letter-spacing:-0.5px;">
                              &#9877; ClinicFlow
                            </span>
                          </td>
                        </tr>
                      </table>
                      <!-- Header title -->
                      <p style="margin:20px 0 0;font-size:22px;font-weight:600;
                                color:#ffffff;letter-spacing:-0.3px;">
                        {headerTitle}
                      </p>
                    </td>
                  </tr>

                  <!-- ── Body ── -->
                  <tr>
                    <td style="background:#ffffff;padding:40px;
                               border-left:1px solid #E5E7EB;
                               border-right:1px solid #E5E7EB;">
                      {bodyHtml}
                    </td>
                  </tr>

                  <!-- ── Footer ── -->
                  <tr>
                    <td style="background:#F9FAFB;border:1px solid #E5E7EB;
                               border-top:none;border-radius:0 0 12px 12px;
                               padding:24px 40px;text-align:center;">
                      <p style="margin:0 0 6px;font-size:12px;color:#9CA3AF;">
                        &copy; {DateTime.UtcNow.Year} ClinicFlow. All rights reserved.
                      </p>
                      <p style="margin:0;font-size:12px;color:#D1D5DB;">
                        This email was sent to you because you have an account with ClinicFlow.
                      </p>
                    </td>
                  </tr>

                </table>
              </td>
            </tr>
          </table>
        </body>
        </html>
        """;

    /// <summary>Teal CTA button used in confirm-email and reset-password emails.</summary>
    private static string PrimaryButton(string label, string url) => $"""
        <table cellpadding="0" cellspacing="0" border="0" style="margin:0 auto;">
          <tr>
            <td align="center"
                style="background:#0D9488;border-radius:8px;
                       box-shadow:0 2px 8px rgba(13,148,136,0.35);">
              <a href="{url}"
                 style="display:inline-block;padding:14px 32px;
                        font-size:15px;font-weight:600;color:#ffffff;
                        text-decoration:none;letter-spacing:0.2px;">
                {label}
              </a>
            </td>
          </tr>
        </table>
        """;

    /// <summary>Appointment detail table shared by confirmation and reminder emails.</summary>
    private static string AppointmentTable(
        string doctorName, DateOnly date, TimeOnly time) => $"""
        <table cellpadding="0" cellspacing="0" border="0"
               style="width:100%;border-collapse:collapse;
                      border-radius:8px;overflow:hidden;
                      border:1px solid #E5E7EB;margin:0 0 8px;">
          <tr style="background:#F0FDFA;">
            <td style="padding:14px 18px;font-size:13px;font-weight:600;
                       color:#0F766E;width:38%;border-bottom:1px solid #E5E7EB;">
              Doctor
            </td>
            <td style="padding:14px 18px;font-size:14px;color:#111827;
                       border-bottom:1px solid #E5E7EB;">
              Dr. {doctorName}
            </td>
          </tr>
          <tr style="background:#ffffff;">
            <td style="padding:14px 18px;font-size:13px;font-weight:600;
                       color:#0F766E;width:38%;border-bottom:1px solid #E5E7EB;">
              Date
            </td>
            <td style="padding:14px 18px;font-size:14px;color:#111827;
                       border-bottom:1px solid #E5E7EB;">
              {date:dddd, MMMM d, yyyy}
            </td>
          </tr>
          <tr style="background:#F0FDFA;">
            <td style="padding:14px 18px;font-size:13px;font-weight:600;
                       color:#0F766E;width:38%;">
              Time
            </td>
            <td style="padding:14px 18px;font-size:14px;color:#111827;">
              {time:HH:mm}
            </td>
          </tr>
        </table>
        """;

    /// <summary>
    /// Single SMTP send method. All public methods funnel through here.
    /// To change the mail provider, only this method needs updating.
    /// </summary>
    private async Task SendAsync(
        string toEmail, string toName, string subject, string htmlBody)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_settings.FromName, _settings.FromAddress));
        message.To.Add(new MailboxAddress(toName, toEmail));
        message.Subject = subject;
        message.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

        using var client = new SmtpClient();

        await client.ConnectAsync(
            _settings.Host,
            _settings.Port,
            SecureSocketOptions.StartTls);   // port 587 — change to SslOnConnect for port 465

        await client.AuthenticateAsync(
            _settings.Username,
            _settings.Password);

        await client.SendAsync(message);
        await client.DisconnectAsync(quit: true);
    }
}