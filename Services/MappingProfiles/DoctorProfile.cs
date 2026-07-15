using Domain.Parameters;
using Shared.DTOs.Doctor;
using Shared.DTOs.Review;
namespace Services.MappingProfiles
{
    public class DoctorProfile : Profile
    {
        public DoctorProfile()
        {
            CreateMap<DoctorFilterRequest, DoctorFilterParams>();
            CreateMap<Domain.Entities.AppModule.DoctorProfile, DoctorSummaryResponse>()
                .ForMember(dest => dest.SpecialtyName, opt => opt.MapFrom(src => src.Specialty.Name))
                .ForMember(dest => dest.FullName, opt => opt.MapFrom(src => $"{src.User.FirstName} {src.User.LastName}"));
            CreateMap<Domain.Entities.AppModule.DoctorProfile, DoctorResponse>()
                .ForMember(dest => dest.SpecialtyName, opt => opt.MapFrom(src => src.Specialty.Name))
                .ForMember(dest => dest.FullName, opt => opt.MapFrom(src => $"{src.User.FirstName} {src.User.LastName}"));
            CreateMap<UpdateDoctorProfileRequest, Domain.Entities.AppModule.DoctorProfile>()
                .ForAllMembers(opt => opt.Condition((src, dest, srcMember) => srcMember is not null)); // Only map non-null properties
            CreateMap<CreateScheduleRequest, DoctorSchedule>()
                .ForMember(dest=>dest.Id, opt => opt.Ignore())
                .ForMember(dest => dest.DoctorProfileId, opt => opt.Ignore());
            CreateMap<DoctorSchedule, ScheduleResponse>();
            CreateMap<UpdateScheduleRequest, DoctorSchedule>()
                .ForMember(dest => dest.Id, opt => opt.Ignore())
                .ForMember(dest => dest.DoctorProfileId, opt => opt.Ignore());
            CreateMap<AppointmentSlot, SlotResponse>();
            CreateMap<ReviewFilterRequest, ReviewFilterParams>();
            CreateMap<Review, ReviewResponse>()
                .ForMember(opt=>opt.PatientName, src => src.MapFrom(src => $"{src.Patient.User.FirstName} {src.Patient.User.LastName}"));


        }
    }
}
