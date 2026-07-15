using Shared.DTOs.Review;

namespace Services.MappingProfiles
{
    public class ReviewProfile : Profile
    {
        public ReviewProfile()
        {
            CreateMap<Review, ReviewResponse>()
                 .ForMember(dest => dest.PatientName,
                  opt => opt.MapFrom(src => $"{src.Patient.User.FirstName} {src.Patient.User.LastName}"));

        }
    }
}
