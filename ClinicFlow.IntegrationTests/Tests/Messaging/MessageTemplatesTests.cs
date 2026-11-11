using ClinicFlow.IntegrationTests.Infrastructure;
using Domain.Enums;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Shared.Authorization;
using Shared.DTOs.Auth;
using Shared.Messaging;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace ClinicFlow.IntegrationTests.Tests.Messaging
{
    // Message templates over HTTP (P1 PR 5; API_CONTRACT.md §5.15): the list, edits with their checks and audit
    // entries, previews and resets, and emails written from them in the recipient's language
    [Collection(nameof(IntegrationTestCollection))]
    public class MessageTemplatesTests(ClinicFlowApiFactory factory) : IAsyncLifetime
    {
        private const string TemplatesUrl = "/api/v2/message-templates";
        private const string ForgotUrl = "/api/auth/forgot-password";

        private readonly HttpClient _client = factory.CreateClient();
        private Guid _patientUserId;
        private string _patientEmail = string.Empty;

        public async Task InitializeAsync()
        {
            await factory.ResetDataBaseAsync();
            _client.DefaultRequestHeaders.Clear();
            await factory.SeedAsync(async db => (_patientUserId, _patientEmail, _) = await SeedDataHelper.SeedPatientUserAsync(db));
        }

        public Task DisposeAsync() => Task.CompletedTask;

        private void AuthenticateAs(string role)
        {
            _client.DefaultRequestHeaders.Clear();
            _client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, Guid.NewGuid().ToString());
            _client.DefaultRequestHeaders.Add(TestAuthHandler.UserRoleHeader, role);
        }

        private async Task<JsonObject> TemplateAsync(string key, string channel, string language)
        {
            var response = await _client.GetAsync($"{TemplatesUrl}?key={key}&channel={channel}&language={language}");
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            return (await response.Content.ReadFromJsonAsync<JsonArray>())!.Should().ContainSingle().Subject!.AsObject();
        }

        private static string IdOf(JsonObject template) => template["id"]!.GetValue<string>();

        private static async Task<JsonObject> BodyAsync(HttpResponseMessage response)
            => (await response.Content.ReadFromJsonAsync<JsonObject>())!;

        // ── List ──────────────────────────────────────────────────────────────

        [Fact]
        public async Task List_Admin_ReturnsEveryMessageOncePerChannelAndLanguage()
        {
            AuthenticateAs(Roles.Admin);

            var response = await _client.GetAsync(TemplatesUrl);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var templates = (await response.Content.ReadFromJsonAsync<JsonArray>())!.Select(t => t!.AsObject()).ToList();
            templates.Should().HaveCount(MessageTemplateCatalog.All.Sum(d => d.Defaults.Count));
            templates.Select(t => t["key"]!.GetValue<string>()).Should().BeInAscendingOrder();

            var first = templates[0]; // AppointmentConfirmed, Email, ar
            IdOf(first).Should().MatchRegex("^[0-9]+$"); // §13.1: ids are strings
            (first["key"]!.GetValue<string>(), first["channel"]!.GetValue<string>(), first["language"]!.GetValue<string>())
                .Should().Be((TemplateKeys.AppointmentConfirmed, "Email", "ar"));
            first["subject"]!.GetValue<string>().Should().Be("تم تأكيد موعدك");
            first["placeholders"]!.AsArray().Select(p => p!.GetValue<string>()).Should().Contain(["clinicName", "patientName", "date", "time"]);
            first["requiredPlaceholders"]!.AsArray().Select(p => p!.GetValue<string>()).Should().Equal("date", "time");
            first["editable"]!.GetValue<bool>().Should().BeTrue();
            first["isDefault"]!.GetValue<bool>().Should().BeTrue();
            first["updatedAt"]!.GetValue<string>().Should().EndWith("Z");

            var whatsApp = templates.First(t => t["channel"]!.GetValue<string>() == "WhatsApp");
            whatsApp["editable"]!.GetValue<bool>().Should().BeFalse();
            whatsApp.ContainsKey("subject").Should().BeFalse();
        }

        [Fact]
        public async Task List_WithFilters_ReturnsTheMatchingTemplates()
        {
            AuthenticateAs(Roles.Admin);

            var sms = await TemplateAsync(TemplateKeys.AppointmentReminder, "Sms", "en");
            var emails = (await (await _client.GetAsync($"{TemplatesUrl}?channel=Email&language=en")).Content.ReadFromJsonAsync<JsonArray>())!;

            sms["body"]!.GetValue<string>().Should().StartWith("{{clinicName}}: reminder of your appointment");
            emails.Select(t => t!["key"]!.GetValue<string>()).Should().Equal(MessageTemplateCatalog.All.Select(d => d.Key).Order());
        }

        [Fact]
        public async Task List_AnUnknownChannel_Returns400()
        {
            AuthenticateAs(Roles.Admin);

            (await _client.GetAsync($"{TemplatesUrl}?channel=Fax")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        [Theory]
        [InlineData(Roles.Manager, HttpStatusCode.OK)]
        [InlineData(Roles.Receptionist, HttpStatusCode.Forbidden)]
        [InlineData(Roles.Doctor, HttpStatusCode.Forbidden)]
        [InlineData(Roles.Patient, HttpStatusCode.Forbidden)]
        public async Task List_NeedsNotificationsTemplates(string role, HttpStatusCode expected)
        {
            AuthenticateAs(role);

            (await _client.GetAsync(TemplatesUrl)).StatusCode.Should().Be(expected);
        }

        [Fact]
        public async Task List_Anonymous_Returns401()
            => (await _client.GetAsync(TemplatesUrl)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // ── Update ────────────────────────────────────────────────────────────

        [Fact]
        public async Task Put_Admin_SavesTheText_AuditsIt_AndTheNextEmailUsesIt()
        {
            AuthenticateAs(Roles.Admin);
            var template = await TemplateAsync(TemplateKeys.PasswordReset, "Email", "ar");

            var response = await _client.PutAsJsonAsync($"{TemplatesUrl}/{IdOf(template)}",
                new { subject = "كلمة مرور جديدة", body = "أهلًا {{name}}\r\n\r\n[اضغط هنا]({{resetUrl}})" });

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var saved = await BodyAsync(response);
            saved["subject"]!.GetValue<string>().Should().Be("كلمة مرور جديدة");
            saved["body"]!.GetValue<string>().Should().Be("أهلًا {{name}}\n\n[اضغط هنا]({{resetUrl}})");
            saved["isDefault"]!.GetValue<bool>().Should().BeFalse();
            (await TemplateAsync(TemplateKeys.PasswordReset, "Email", "ar"))["subject"]!.GetValue<string>().Should().Be("كلمة مرور جديدة");

            await factory.SeedAsync(async db =>
            {
                var entry = (await db.AuditEntries.AsNoTracking().Where(e => e.EntityType == "MessageTemplate").ToListAsync()).Should().ContainSingle().Subject;
                entry.EntityId.Should().Be(IdOf(template));
                entry.Changes.Should().Contain("Subject").And.Contain("Body").And.Contain("IsDefault");
            });

            // The patient reads Arabic (the default), so the reset email is written from the edited template
            _client.DefaultRequestHeaders.Clear();
            await _client.PostAsJsonAsync(ForgotUrl, new ForgotPasswordRequest(_patientEmail));
            await factory.DrainOutboxAsync();

            var email = factory.Emails.Sent.Should().ContainSingle().Subject;
            email.Message!.Subject.Should().Be("كلمة مرور جديدة");
            email.Message.TextBody.Should().StartWith("أهلًا Test Patient\n\nاضغط هنا: http");
            (email.UserId, email.Kind).Should().Be((_patientUserId, SentEmailKind.PasswordReset));
        }

        [Fact]
        public async Task Put_WithoutASubject_KeepsIt()
        {
            AuthenticateAs(Roles.Admin);
            var template = await TemplateAsync(TemplateKeys.AppointmentReminder, "Email", "en");

            var response = await _client.PutAsJsonAsync($"{TemplatesUrl}/{IdOf(template)}", new { body = "See you on {{date}} at {{time}}." });

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            (await BodyAsync(response))["subject"]!.GetValue<string>().Should().Be(template["subject"]!.GetValue<string>());
        }

        [Fact]
        public async Task Put_APlaceholderTheMessageDoesNotOffer_Returns400WithItsName()
        {
            AuthenticateAs(Roles.Admin);
            var template = await TemplateAsync(TemplateKeys.AppointmentReminder, "Sms", "ar");

            var response = await _client.PutAsJsonAsync($"{TemplatesUrl}/{IdOf(template)}", new { body = "{{date}} {{time}} {{resetUrl}}" });

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var problem = await BodyAsync(response);
            problem["title"]!.GetValue<string>().Should().Be("Template.UnknownPlaceholder"); // §13.1: a coded 400 is shape A
            problem["placeholder"]!.GetValue<string>().Should().Be("resetUrl");
            (await TemplateAsync(TemplateKeys.AppointmentReminder, "Sms", "ar"))["isDefault"]!.GetValue<bool>().Should().BeTrue();
        }

        [Fact]
        public async Task Put_ABodyWithoutARequiredPlaceholder_Returns400WithItsName()
        {
            AuthenticateAs(Roles.Admin);
            var template = await TemplateAsync(TemplateKeys.EmailConfirmation, "Email", "en");

            var response = await _client.PutAsJsonAsync($"{TemplatesUrl}/{IdOf(template)}", new { body = "Welcome, {{name}}!" });

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var problem = await BodyAsync(response);
            problem["title"]!.GetValue<string>().Should().Be("Template.RequiredPlaceholderMissing");
            problem["placeholder"]!.GetValue<string>().Should().Be("confirmUrl");
        }

        [Fact]
        public async Task Put_AnEmptyBody_Returns400KeyedByTheField()
        {
            AuthenticateAs(Roles.Admin);
            var template = await TemplateAsync(TemplateKeys.EmailConfirmation, "Email", "en");

            var response = await _client.PutAsJsonAsync($"{TemplatesUrl}/{IdOf(template)}", new { subject = "", body = "" });

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var errors = (await BodyAsync(response))["errors"]!.AsObject();
            errors.ContainsKey("body").Should().BeTrue();
            errors.ContainsKey("subject").Should().BeTrue();
        }

        [Fact]
        public async Task Put_AWhatsAppTemplate_Returns409()
        {
            AuthenticateAs(Roles.Admin);
            var template = await TemplateAsync(TemplateKeys.AppointmentConfirmed, "WhatsApp", "ar");

            var response = await _client.PutAsJsonAsync($"{TemplatesUrl}/{IdOf(template)}", new { body = "{{date}} {{time}}" });

            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await BodyAsync(response))["title"]!.GetValue<string>().Should().Be("Template.NotEditable");
        }

        [Fact]
        public async Task Put_AnUnknownTemplate_Returns404()
        {
            AuthenticateAs(Roles.Admin);

            var response = await _client.PutAsJsonAsync($"{TemplatesUrl}/999999", new { body = "x" });

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await BodyAsync(response))["title"]!.GetValue<string>().Should().Be("Template.NotFound");
        }

        [Fact]
        public async Task Put_Receptionist_Returns403()
        {
            AuthenticateAs(Roles.Admin);
            var template = await TemplateAsync(TemplateKeys.AppointmentReminder, "Sms", "ar");
            AuthenticateAs(Roles.Receptionist);

            (await _client.PutAsJsonAsync($"{TemplatesUrl}/{IdOf(template)}", new { body = "{{date}} {{time}}" }))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        // ── Preview ───────────────────────────────────────────────────────────

        [Fact]
        public async Task Preview_AnEmail_ReturnsTheTextAndTheHtmlWithSamples_AndSavesNothing()
        {
            AuthenticateAs(Roles.Admin);
            var template = await TemplateAsync(TemplateKeys.AppointmentConfirmed, "Email", "ar");

            var response = await _client.PostAsJsonAsync($"{TemplatesUrl}/{IdOf(template)}/preview",
                new { subject = "موعدك لدى {{clinicName}}", body = "{{patientName}}: {{date}} {{time}}" });

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var preview = await BodyAsync(response);
            preview["subject"]!.GetValue<string>().Should().Be("موعدك لدى عيادة الشفاء");
            preview["body"]!.GetValue<string>().Should().Be("منى حسن: الأحد، 15 نوفمبر 2026 10:30 ص");
            preview["html"]!.GetValue<string>().Should().Contain("dir=\"rtl\"").And.Contain("منى حسن");
            preview.ContainsKey("smsSegments").Should().BeFalse();
            (await TemplateAsync(TemplateKeys.AppointmentConfirmed, "Email", "ar"))["body"]!.GetValue<string>()
                .Should().Be(template["body"]!.GetValue<string>());
        }

        [Fact]
        public async Task Preview_AnSms_ReturnsHowManyPartsItTakes()
        {
            AuthenticateAs(Roles.Admin);
            var template = await TemplateAsync(TemplateKeys.AppointmentConfirmed, "Sms", "ar");

            var response = await _client.PostAsJsonAsync($"{TemplatesUrl}/{IdOf(template)}/preview", new { body = template["body"]!.GetValue<string>() });

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var preview = await BodyAsync(response);
            preview["smsSegments"]!.GetValue<int>().Should().Be(2); // about 100 Arabic characters with the samples
            preview["body"]!.GetValue<string>().Should().StartWith("عيادة الشفاء: تم حجز موعدك");
            preview.ContainsKey("html").Should().BeFalse();
        }

        [Fact]
        public async Task Preview_FailsAsSavingWould()
        {
            AuthenticateAs(Roles.Admin);
            var template = await TemplateAsync(TemplateKeys.AppointmentConfirmed, "Email", "en");

            var response = await _client.PostAsJsonAsync($"{TemplatesUrl}/{IdOf(template)}/preview", new { body = "{{date}}" });

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await BodyAsync(response))["title"]!.GetValue<string>().Should().Be("Template.RequiredPlaceholderMissing");
        }

        // ── Reset ─────────────────────────────────────────────────────────────

        [Fact]
        public async Task Reset_PutsTheDefaultTextBack()
        {
            AuthenticateAs(Roles.Admin);
            var template = await TemplateAsync(TemplateKeys.AppointmentReminder, "Sms", "en");
            await _client.PutAsJsonAsync($"{TemplatesUrl}/{IdOf(template)}", new { body = "{{date}} {{time}}" });

            var response = await _client.PostAsync($"{TemplatesUrl}/{IdOf(template)}/reset", null);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var reset = await BodyAsync(response);
            reset["body"]!.GetValue<string>().Should().Be(template["body"]!.GetValue<string>());
            reset["isDefault"]!.GetValue<bool>().Should().BeTrue();
        }

        [Fact]
        public async Task Reset_AnUnknownTemplate_Returns404()
        {
            AuthenticateAs(Roles.Admin);

            var response = await _client.PostAsync($"{TemplatesUrl}/999999/reset", null);

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await BodyAsync(response))["title"]!.GetValue<string>().Should().Be("Template.NotFound");
        }

        // ── Emails in the recipient's language (R10) ──────────────────────────

        [Fact]
        public async Task AnEmail_ByDefault_IsArabicAndRightToLeft()
        {
            await _client.PostAsJsonAsync(ForgotUrl, new ForgotPasswordRequest(_patientEmail));
            await factory.DrainOutboxAsync();

            var email = factory.Emails.Sent.Should().ContainSingle().Subject.Message!;
            email.Subject.Should().Be("إعادة تعيين كلمة المرور");
            email.ToName.Should().Be("Test Patient");
            email.TextBody.Should().StartWith("مرحبًا Test Patient،");
            email.HtmlBody.Should().Contain("<html lang=\"ar\" dir=\"rtl\">");
        }

        [Fact]
        public async Task AnEmail_ToAUserWhoReadsEnglish_IsEnglish()
        {
            await factory.SeedAsync(async db =>
            {
                (await db.Users.SingleAsync(u => u.Id == _patientUserId)).PreferredLanguage = Language.En;
                await db.SaveChangesAsync();
            });

            await _client.PostAsJsonAsync(ForgotUrl, new ForgotPasswordRequest(_patientEmail));
            await factory.DrainOutboxAsync();

            var sent = factory.Emails.Sent.Should().ContainSingle().Subject;
            sent.Message!.Subject.Should().Be("Reset your password");
            sent.Message.HtmlBody.Should().Contain("<html lang=\"en\" dir=\"ltr\">");
            sent.Token.Should().NotBeNullOrEmpty(); // the link's token still reads from the text
        }

        [Fact]
        public async Task TheLanguage_IsStoredAsItsCode()
        {
            await factory.SeedAsync(async db =>
            {
                var codes = await db.Database.SqlQuery<string>($"SELECT DISTINCT [Language] AS [Value] FROM MessageTemplates").ToListAsync();
                codes.Should().BeEquivalentTo(["ar", "en"]);
                var user = await db.Database.SqlQuery<string>($"SELECT PreferredLanguage + '/' + NotificationChannel AS [Value] FROM Users").SingleAsync();
                user.Should().Be("ar/Both");
            });
        }
    }
}
