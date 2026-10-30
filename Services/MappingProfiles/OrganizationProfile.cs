using Domain.Entities.Tenancy;
using Shared.DTOs.Organization;

namespace Services.MappingProfiles
{
    // The organization, its branches and rooms (API_CONTRACT.md §13.24, §13.25). Optional columns are sent as ""
    public class OrganizationProfile : Profile
    {
        public OrganizationProfile()
        {
            CreateMap<Organization, OrganizationProfileResponse>()
                .ForCtorParam(nameof(OrganizationProfileResponse.TaxNumber), o => o.MapFrom(s => s.TaxRegistrationNo ?? ""))
                .ForCtorParam(nameof(OrganizationProfileResponse.CommercialRegistration), o => o.MapFrom(s => s.CommercialRegistrationNo ?? ""))
                .ForCtorParam(nameof(OrganizationProfileResponse.Phone), o => o.MapFrom(s => s.Phone ?? ""))
                .ForCtorParam(nameof(OrganizationProfileResponse.Email), o => o.MapFrom(s => s.Email ?? ""));

            CreateMap<Branch, BranchResponse>()
                .ForCtorParam(nameof(BranchResponse.AddressEn), o => o.MapFrom(s => s.AddressEn ?? ""))
                .ForCtorParam(nameof(BranchResponse.AddressAr), o => o.MapFrom(s => s.AddressAr ?? ""))
                .ForCtorParam(nameof(BranchResponse.Phone), o => o.MapFrom(s => s.Phone ?? ""))
                .ForCtorParam(nameof(BranchResponse.TimeZone), o => o.MapFrom(s => s.TimeZoneId));

            CreateMap<Room, RoomResponse>();
        }
    }
}
