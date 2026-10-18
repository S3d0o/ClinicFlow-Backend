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

            // Doctor.User and Patient.User must be loaded for the names
            body[0].DoctorName.Should().Be("Test Doctor");
            body[0].PatientName.Should().Be("Test Patient");
        }
        [Fact]
        public async Task GetPatientAppointments_NoQueryString_Returns200WithTheFirstPage()
        {
            // REVIEW.md H6: PageNumber/PageSize defaulted to 0, so Take(0) returned an empty list
            _client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, _patientUserId.ToString());
            _client.DefaultRequestHeaders.Add(TestAuthHandler.UserRoleHeader, "Patient");

            var response = await _client.GetAsync(BaseUrl);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            (await response.Content.ReadFromJsonAsync<List<JsonElement>>()).Should().HaveCount(1);
        }

        [Theory]
        [InlineData("PageNumber=0&PageSize=10")]
        [InlineData("PageNumber=1&PageSize=51")]
        public async Task GetPatientAppointments_PagingOutOfRange_Returns400(string query)
        {
            // REVIEW.md H6: PageNumber=0 used to reach Skip(-10) and return 500
            _client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, _patientUserId.ToString());
            _client.DefaultRequestHeaders.Add(TestAuthHandler.UserRoleHeader, "Patient");

            var response = await _client.GetAsync($"{BaseUrl}?{query}");

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
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
