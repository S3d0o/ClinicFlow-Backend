using Domain.Parameters;
using Shared.DTOs.Appointment;

namespace Services.MappingProfiles
{
    public class AppointmentProfile : Profile
    {
        public AppointmentProfile()
        {
            CreateMap<Appointment, AppointmentResponse>()
                 .ForMember(dest => dest.DoctorName, opt => opt.MapFrom(src => $"{src.Doctor.User.FirstName} {src.Doctor.User.LastName}"))
                 .ForMember(dest => dest.PatientName, opt => opt.MapFrom(src => $"{src.Patient.User.FirstName} {src.Patient.User.LastName}"))
                 .ForMember(dest => dest.AppointmentDate, opt => opt.MapFrom(src => src.Slot.Date))
                 .ForMember(dest => dest.StartTime, opt => opt.MapFrom(src => src.Slot.StartTime))
                 .ForMember(dest => dest.EndTime, opt => opt.MapFrom(src => src.Slot.EndTime))
                 .ForMember(dest => dest.HasReview,opt => opt.MapFrom(src => src.Review != null)); 
            CreateMap<AppointmentFilterRequest, AppointmentFilterParams>();
        }
    }
}
