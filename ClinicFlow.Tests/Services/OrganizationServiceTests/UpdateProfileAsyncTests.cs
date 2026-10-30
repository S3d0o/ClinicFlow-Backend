using Domain.Entities.Tenancy;
using NSubstitute;
using Shared.DTOs.Organization;

namespace ClinicFlow.Tests.Services.OrganizationServiceTests
{
    public class UpdateProfileAsyncTests : OrganizationServiceTestsBase
    {
        [Fact]
        public async Task UpdateProfileAsync_ValidRequest_ReplacesTheProfileAndSaves()
        {
            var organization = new Organization { Id = Organization.DefaultId, LegalName = "Old", BrandNameEn = "Old" };
            _uow.Organizations.GetCurrentAsync(Arg.Any<CancellationToken>()).Returns(organization);
            var request = new OrganizationProfileRequest(
                "Nile Clinics LLC", "Nile Clinics", "عيادات النيل", "123-456-789", "CR 4521", "+20225551234", "hello@nile.example");

            var result = await _sut.UpdateProfileAsync(request, CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.Equal("Nile Clinics LLC", organization.LegalName);
            Assert.Equal("عيادات النيل", organization.BrandNameAr);
            Assert.Equal("123-456-789", organization.TaxRegistrationNo);
            Assert.Equal("CR 4521", organization.CommercialRegistrationNo);
            Assert.Equal(request.LegalName, result.Value.LegalName);
            Assert.Equal(request.TaxNumber, result.Value.TaxNumber);
            Assert.Equal(request.Email, result.Value.Email);
            await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        }
    }
}
