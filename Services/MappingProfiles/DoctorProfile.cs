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
            // Partial update: omitted fields keep their value. ForAllMembers(Condition(srcMember is not null))
            // does not work here — for nullable value types (TimeOnly? -> TimeOnly) AutoMapper still writes default.
            CreateMap<UpdateScheduleRequest, DoctorSchedule>()
                .ForMember(dest => dest.Id, opt => opt.Ignore())
                .ForMember(dest => dest.DoctorProfileId, opt => opt.Ignore())
                .ForMember(dest => dest.StartTime, opt => { opt.PreCondition(src => src.StartTime.HasValue); opt.MapFrom(src => src.StartTime!.Value); })
                .ForMember(dest => dest.EndTime, opt => { opt.PreCondition(src => src.EndTime.HasValue); opt.MapFrom(src => src.EndTime!.Value); })
                .ForMember(dest => dest.SlotDurationMinutes, opt => { opt.PreCondition(src => src.SlotDurationMinutes.HasValue); opt.MapFrom(src => src.SlotDurationMinutes!.Value); })
                .ForMember(dest => dest.IsActive, opt => { opt.PreCondition(src => src.IsActive.HasValue); opt.MapFrom(src => src.IsActive!.Value); });
            CreateMap<AppointmentSlot, SlotResponse>();
            CreateMap<ReviewFilterRequest, ReviewFilterParams>();
            CreateMap<Review, ReviewResponse>()
                .ForMember(opt=>opt.PatientName, src => src.MapFrom(src => $"{src.Patient.User.FirstName} {src.Patient.User.LastName}"));


        }
    }
}
