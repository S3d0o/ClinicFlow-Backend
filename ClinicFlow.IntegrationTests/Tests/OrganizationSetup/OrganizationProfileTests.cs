using ClinicFlow.IntegrationTests.Infrastructure;
using FluentAssertions;
using Shared.Authorization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace ClinicFlow.IntegrationTests.Tests.OrganizationSetup
{
    // The organization's profile over HTTP (P1 PR 1; API_CONTRACT.md §13.24)
    [Collection(nameof(IntegrationTestCollection))]
    public class OrganizationProfileTests(ClinicFlowApiFactory factory) : IAsyncLifetime
    {
        private const string Url = "/api/v2/organization";

        private readonly HttpClient _client = factory.CreateClient();

        public async Task InitializeAsync()
        {
            await factory.ResetDataBaseAsync();
            _client.DefaultRequestHeaders.Clear();
        }

        public Task DisposeAsync() => Task.CompletedTask;

        private void AuthenticateAs(string role)
        {
            _client.DefaultRequestHeaders.Clear();
            _client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, Guid.NewGuid().ToString());
            _client.DefaultRequestHeaders.Add(TestAuthHandler.UserRoleHeader, role);
        }

        private static object AProfile(string email = "hello@nile.example") => new
        {
            legalName = "Nile Clinics LLC",
            brandNameEn = "Nile Clinics",
            brandNameAr = "عيادات النيل",
            taxNumber = "123-456-789",
            commercialRegistration = "CR 4521",
            phone = "+20225551234",
            email,
        };

        [Fact]
        public async Task Update_Admin_Returns200AndTheProfileReadsBack()
        {
            AuthenticateAs(Roles.Admin);

            var response = await _client.PutAsJsonAsync(Url, AProfile());

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var profile = (await _client.GetFromJsonAsync<JsonObject>(Url))!;
            profile["brandNameAr"]!.GetValue<string>().Should().Be("عيادات النيل");
            profile["taxNumber"]!.GetValue<string>().Should().Be("123-456-789");
        }

        [Fact]
        public async Task Update_InvalidEmail_Returns400KeyedByEmail()
        {
            AuthenticateAs(Roles.Admin);

            var response = await _client.PutAsJsonAsync(Url, AProfile(email: "not-an-email"));

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await response.Content.ReadFromJsonAsync<JsonObject>())!["errors"]!.AsObject().Select(e => e.Key)
                .Should().BeEquivalentTo(["email"]);
        }

        [Fact]
        public async Task Get_Doctor_Returns403()
        {
            AuthenticateAs(Roles.Doctor);

            var response = await _client.GetAsync(Url);

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
    }
}
