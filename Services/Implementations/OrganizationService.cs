using Shared.DTOs.Organization;

namespace Services.Implementations
{
    // The organization's profile (FR-ORG-01). [HasPermission(org.settings.manage)] guards it; the organization is the
    // whole tenant, so there is no record scope to check
    public class OrganizationService(
        IUnitOfWork uow,
        IMapper mapper,
        ILogger<OrganizationService> logger) : IOrganizationService
    {
        public async Task<Result<OrganizationProfileResponse>> UpdateProfileAsync(OrganizationProfileRequest request, CancellationToken ct)
        {
            var organization = await uow.Organizations.GetCurrentAsync(ct);

            organization.LegalName = request.LegalName;
            organization.BrandNameEn = request.BrandNameEn;
            organization.BrandNameAr = request.BrandNameAr;
            organization.TaxRegistrationNo = request.TaxNumber;
            organization.CommercialRegistrationNo = request.CommercialRegistration;
            organization.Phone = request.Phone;
            organization.Email = request.Email;
            await uow.SaveChangesAsync(ct);

            logger.LogInformation("Organization {OrganizationId} profile updated", organization.Id);
            return mapper.Map<OrganizationProfileResponse>(organization);
        }
    }
}
