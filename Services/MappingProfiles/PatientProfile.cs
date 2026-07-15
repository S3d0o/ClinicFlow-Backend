using Shared.DTOs.Patient;

namespace Services.MappingProfiles
{
    public class PatientProfile : Profile
    {
        public PatientProfile()
        {
            CreateMap<Domain.Entities.AppModule.PatientProfile, PatientProfileResponse>()
                .ForMember(dest => dest.FullName, opt => opt.MapFrom(src => $"{src.User.FirstName} {src.User.LastName}"))
                .ForMember(dest => dest.Email, opt => opt.MapFrom(src => src.User.Email))
                .ForMember(dest => dest.PhoneNumber, opt => opt.MapFrom(src => src.User.PhoneNumber));

            CreateMap<UpdatePatientProfileRequest, Domain.Entities.AppModule.PatientProfile>()
                .ForAllMembers(opt=>opt.Condition((src,dest,srcMember)=> srcMember is not null));
                
        }
    }
}
