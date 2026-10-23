using ClinicFlow.IntegrationTests.Infrastructure;
using FluentAssertions;
using Shared.DTOs.Appointment;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace ClinicFlow.IntegrationTests.Tests.Serialization
{
    // Timestamps are stored in UTC, and SQL Server keeps no DateTimeKind, so before contract 4.0.0-p0 the ones read
    // back from the database were sent without a "Z" (REVIEW.md §4.1). They must now always carry it.
    [Collection(nameof(IntegrationTestCollection))]
    public class TimestampSerializationTests : IAsyncLifetime
    {
        private Guid _patientUserId;
        private int _appointmentId;

        private readonly ClinicFlowApiFactory _factory;
        private readonly HttpClient _client;

        public TimestampSerializationTests(ClinicFlowApiFactory factory)
        {
            _factory = factory;
            _client = factory.CreateClient();
        }

        public async Task InitializeAsync()
        {
            await _factory.ResetDataBaseAsync();
            _client.DefaultRequestHeaders.Clear();

            await _factory.SeedAsync(async db =>
            {
                (_patientUserId, _, _, _appointmentId) = await SeedDataHelper.SeedBaseAppointmentAsync(db);
            });

            // Cancelling writes CancelledAt from the clock and a notification, both read back below
            _client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, _patientUserId.ToString());
            _client.DefaultRequestHeaders.Add(TestAuthHandler.UserRoleHeader, "Patient");
            var cancel = await _client.PostAsJsonAsync($"/api/appointments/{_appointmentId}/cancel", new CancelAppointmentRequest("Travelling"));
            cancel.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public async Task GetAppointment_TimestampsFromTheDatabase_EndInZ()
        {
            using var body = await GetJsonAsync($"/api/appointments/{_appointmentId}");

            AssertUtc(body.RootElement.GetProperty("bookedAt"));
            var cancelledAt = AssertUtc(body.RootElement.GetProperty("cancelledAt"));

            // UTC, not the clinic's wall clock (UTC+2 or +3)
            cancelledAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5));
        }

        [Fact]
        public async Task GetMyNotifications_CreatedAt_EndsInZ()
        {
            using var body = await GetJsonAsync("/api/notifications/my");

            body.RootElement.GetArrayLength().Should().BePositive();
            foreach (var notification in body.RootElement.EnumerateArray())
                AssertUtc(notification.GetProperty("createdAt"));
        }

        [Fact]
        public async Task GetMyProfile_CreatedAt_EndsInZ()
        {
            using var body = await GetJsonAsync("/api/profile/me");

            AssertUtc(body.RootElement.GetProperty("createdAt"));
        }

        private async Task<JsonDocument> GetJsonAsync(string url)
        {
            var response = await _client.GetAsync(url);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        }

        private static DateTimeOffset AssertUtc(JsonElement timestamp)
        {
            var text = timestamp.GetString();
            text.Should().EndWith("Z", "every API timestamp is UTC and says so");
            return DateTimeOffset.Parse(text!, CultureInfo.InvariantCulture);
        }
    }
}
