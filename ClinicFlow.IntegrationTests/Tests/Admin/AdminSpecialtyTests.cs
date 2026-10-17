using ClinicFlow.IntegrationTests.Infrastructure;
using Domain.Entities.AppModule;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Shared.DTOs.Specialty;
using System.Net;
using System.Net.Http.Json;

namespace ClinicFlow.IntegrationTests.Tests.Admin
{
    [Collection(nameof(IntegrationTestCollection))]
    public class AdminSpecialtyTests : IAsyncLifetime
    {
        private const string AdminUrl = "/api/admin/specialties";

        private int _cardiologyId;
        private int _unusedId;

        private readonly ClinicFlowApiFactory _factory;
        private readonly HttpClient _client;

        public AdminSpecialtyTests(ClinicFlowApiFactory factory)
        {
            _factory = factory;
            _client = factory.CreateClient();
        }

        public async Task InitializeAsync()
        {
            await _factory.ResetDataBaseAsync();
            _client.DefaultRequestHeaders.Clear();
            _client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, Guid.NewGuid().ToString());
            _client.DefaultRequestHeaders.Add(TestAuthHandler.UserRoleHeader, "Admin");

            await _factory.SeedAsync(async db =>
            {
                // Cardiology has a doctor; Dermatology has none
                await SeedDataHelper.SeedDoctorAsync(db, "cardio", specialtyName: "Cardiology");
                var unused = new Specialty { Name = "Dermatology", IsActive = true };
                db.Specialties.Add(unused);
                await db.SaveChangesAsync();
                _cardiologyId = db.Specialties.Single(s => s.Name == "Cardiology").Id;
                _unusedId = unused.Id;
            });
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public async Task Create_NewName_Returns201AndAppearsInThePublicList()
        {
            var response = await _client.PostAsJsonAsync(AdminUrl, new SpecialtyRequest("Neurology", "Brain and nerves", null));

            response.StatusCode.Should().Be(HttpStatusCode.Created);
            var created = await response.Content.ReadFromJsonAsync<SpecialtyResponse>();
            created!.Name.Should().Be("Neurology");
            created.IsActive.Should().BeTrue();

            _client.DefaultRequestHeaders.Clear();
            var publicList = await _client.GetFromJsonAsync<List<SpecialtyResponse>>("/api/Specialty");
            publicList!.Select(s => s.Name).Should().Contain("Neurology");
        }

        [Fact]
        public async Task Create_ExistingNameInAnotherCase_Returns409NameAlreadyExists()
        {
            // SQL Server's default collation is case-insensitive, so "cardiology" matches "Cardiology".
            var response = await _client.PostAsJsonAsync(AdminUrl, new SpecialtyRequest("cardiology", null, null));

            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await response.Content.ReadAsStringAsync()).Should().Contain("Specialty.NameAlreadyExists");
            await _factory.SeedAsync(async db =>
                (await db.Specialties.CountAsync(s => s.Name == "Cardiology")).Should().Be(1));
        }

        [Fact]
        public async Task Update_RenameToAnotherSpecialtysName_Returns409AndKeepsTheOldName()
        {
            var response = await _client.PutAsJsonAsync($"{AdminUrl}/{_unusedId}", new SpecialtyRequest("Cardiology", null, null));

            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await response.Content.ReadAsStringAsync()).Should().Contain("Specialty.NameAlreadyExists");
            await _factory.SeedAsync(async db =>
                (await db.Specialties.AsNoTracking().SingleAsync(s => s.Id == _unusedId)).Name.Should().Be("Dermatology"));
        }

        [Fact]
        public async Task Update_NewDescription_Returns200WithTheChange()
        {
            var response = await _client.PutAsJsonAsync($"{AdminUrl}/{_unusedId}", new SpecialtyRequest("Dermatology", "Skin, hair and nails", null));

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            (await response.Content.ReadFromJsonAsync<SpecialtyResponse>())!.Description.Should().Be("Skin, hair and nails");
        }

        [Fact]
        public async Task Delete_SpecialtyWithDoctors_Returns409AndKeepsIt()
        {
            var response = await _client.DeleteAsync($"{AdminUrl}/{_cardiologyId}");

            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await response.Content.ReadAsStringAsync()).Should().Contain("Specialty.HasAssignedDoctors");
            await _factory.SeedAsync(async db =>
                (await db.Specialties.AnyAsync(s => s.Id == _cardiologyId)).Should().BeTrue());
        }

        [Fact]
        public async Task Delete_UnusedSpecialty_Returns204AndRemovesIt()
        {
            var response = await _client.DeleteAsync($"{AdminUrl}/{_unusedId}");

            response.StatusCode.Should().Be(HttpStatusCode.NoContent);
            await _factory.SeedAsync(async db =>
                (await db.Specialties.AnyAsync(s => s.Id == _unusedId)).Should().BeFalse());
        }

        [Fact]
        public async Task Delete_UnknownSpecialty_Returns404()
        {
            var response = await _client.DeleteAsync($"{AdminUrl}/999999");

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await response.Content.ReadAsStringAsync()).Should().Contain("Specialty.NotFound");
        }
    }
}
