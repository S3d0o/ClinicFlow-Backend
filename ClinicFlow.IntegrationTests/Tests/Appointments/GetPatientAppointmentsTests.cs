using ClinicFlow.IntegrationTests.Infrastructure;
using FluentAssertions;
using Shared.DTOs.Appointment;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClinicFlow.IntegrationTests.Tests.Appointments
{
    [Collection(nameof(IntegrationTestCollection))]
    public class GetPatientAppointmentsTests : IAsyncLifetime
    {
        private const string BaseUrl = "/api/appointments/my";

        private Guid _patientUserId;
        private Guid _emptyPatientUserId;
        private readonly ClinicFlowApiFactory _factory;
        private readonly HttpClient _client;

        public GetPatientAppointmentsTests(ClinicFlowApiFactory factory)
        {
            _factory = factory;
            _client = factory.CreateClient();
        }
        public Task DisposeAsync() => Task.CompletedTask;

        public async Task InitializeAsync()
        {
            await _factory.ResetDataBaseAsync();
            _client.DefaultRequestHeaders.Clear();

            await _factory.SeedAsync(async db =>
            {       
                
                // patient with one appointment
                (_patientUserId,_,_,_)
                = await SeedDataHelper.SeedBaseAppointmentAsync(db);

                // patient with no appointments
                _emptyPatientUserId =
                    await SeedDataHelper.SeedPatientOnlyAsync(db);
            });
        }

        [Fact]
        public async Task GetPatientAppointments_ValidRequest_Returns200WithAppointments()
        {

            // Arrange
            _client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, _patientUserId.ToString());
            _client.DefaultRequestHeaders.Add(TestAuthHandler.UserRoleHeader, "Patient");

            var request = new AppointmentFilterRequest()
            {
                PageSize = 10,
                PageNumber = 1,
            };

            // Act

            var response = await _client.GetAsync($"{BaseUrl}?PageSize={request.PageSize}&PageNumber={request.PageNumber}");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            //Every test that deserializes a response containing an enum will need these options.
            //To avoid repeating this every time, add a static helper to your test infrastructure:
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            options.Converters.Add(new JsonStringEnumConverter());

            var body = await response.Content.ReadFromJsonAsync<List<AppointmentResponse>>(options);

            body.Should().NotBeNull();
            body!.Should().NotBeEmpty();
            body!.Count.Should().Be(1);

        }
        [Fact]
        public async Task GetPatientAppointments_WrongRole_Returns403()
        {

            // Arrange
            _client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, _patientUserId.ToString());
            _client.DefaultRequestHeaders.Add(TestAuthHandler.UserRoleHeader, "Doctor");

            var request = new AppointmentFilterRequest()
            {
                PageSize = 10,
                PageNumber = 1,
            };

            // Act

            var response = await _client.GetAsync($"{BaseUrl}?PageSize={request.PageSize}&PageNumber={request.PageNumber}");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
        [Fact]
        public async Task GetPatientAppointments_Unauthenticated_Returns401()
        {

            // Arrange

            var request = new AppointmentFilterRequest()
            {
                PageSize = 10,
                PageNumber = 1,
            };

            // Act

            var response = await _client.GetAsync($"{BaseUrl}?PageSize={request.PageSize}&PageNumber={request.PageNumber}");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
        [Fact]
        public async Task GetPatientAppointments_NoAppointments_Returns200EmptyList()
        {

            // Arrange

            _client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, _emptyPatientUserId.ToString());
            _client.DefaultRequestHeaders.Add(TestAuthHandler.UserRoleHeader, "Patient");

            var request = new AppointmentFilterRequest()
            {
                PageSize = 10,
                PageNumber = 1,
            };

            // Act

            var response = await _client.GetAsync($"{BaseUrl}?PageSize={request.PageSize}&PageNumber={request.PageNumber}");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            (await response.Content.ReadFromJsonAsync<List<AppointmentResponse>>()).Should().BeEmpty();
        }
    }
}
