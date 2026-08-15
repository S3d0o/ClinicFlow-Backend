using ClinicFlow.Domain.Enums;
using ClinicFlow.IntegrationTests.Infrastructure;
using Domain.Enums;
using FluentAssertions;
using Shared.DTOs.Appointment;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit.Abstractions;

namespace ClinicFlow.IntegrationTests.Tests.Appointments
{
    [Collection(nameof(IntegrationTestCollection))]
    public class BookAppointmentTests : IAsyncLifetime
    {
        private const string BaseUrl = "/api/appointments";

        private Guid _patientUserId;
        private int _slotId;

        private readonly ClinicFlowApiFactory _factory;
        private readonly HttpClient _client;
        private readonly ITestOutputHelper _output;

        public BookAppointmentTests(ClinicFlowApiFactory factory, ITestOutputHelper output)
        {
            _factory = factory;
            _client = factory.CreateClient();
            _output = output;
        }

        public async Task InitializeAsync()
        {
            await _factory.ResetDataBaseAsync();
            _client.DefaultRequestHeaders.Clear();

            await _factory.SeedAsync(async db =>
            {
                (_patientUserId, _, _slotId, _) =
                await SeedDataHelper.SeedBaseAppointmentAsync(db);
            });
        }

        public Task DisposeAsync() => Task.CompletedTask;

        // ----------------------------------------------------------------

        [Fact]
        public async Task BookAppointment_ValidRequest_Returns201WithAppointmentDetails()
        {
            // Arrange
            _client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, _patientUserId.ToString());
            _client.DefaultRequestHeaders.Add(TestAuthHandler.UserRoleHeader, "Patient");

            var request = new BookAppointmentRequest(_slotId, "First visit");

            // Act
            var response = await _client.PostAsJsonAsync(BaseUrl, request);
            _output.WriteLine($"Status: {response.StatusCode}, Body: {await response.Content.ReadAsStringAsync()}");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.Created);

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            options.Converters.Add(new JsonStringEnumConverter());

            var body = await response.Content.ReadFromJsonAsync<AppointmentResponse>(options);
            body.Should().NotBeNull();
            body!.PatientProfileId.Should().BeGreaterThan(0);
            body.Status.Should().Be(AppointmentStatus.Confirmed);
        }

        [Fact]
        public async Task BookAppointment_SlotNotFound_Returns404()
        {
            // Arrange
            _client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, _patientUserId.ToString());
            _client.DefaultRequestHeaders.Add(TestAuthHandler.UserRoleHeader, "Patient");

            var request = new BookAppointmentRequest(9999, "First visit");

            // Act
            var response = await _client.PostAsJsonAsync(BaseUrl, request);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task BookAppointment_Unauthenticated_Returns401()
        {
            // Arrange — no auth headers
            var request = new BookAppointmentRequest(9999, "First visit");

            // Act
            var response = await _client.PostAsJsonAsync(BaseUrl, request);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
    }
}