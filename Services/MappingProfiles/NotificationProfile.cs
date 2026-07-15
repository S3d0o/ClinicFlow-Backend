using Domain.Entities.IdentityModule;
using Shared.DTOs.Notification;

namespace Services.MappingProfiles
{
    public class NotificationProfile : Profile
    {
        public NotificationProfile()
        {
            CreateMap<CreateNotificationRequest, Notification>();
            CreateMap<Notification, NotificationResponse>()
                .ForMember(dest => dest.Type, opt => opt.MapFrom(src => src.Type.ToString()));
        }
    }
}
