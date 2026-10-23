using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Services.Implementations;
using Shared.Settings;

namespace Services.Extensions
{
    public static class CoreServiceExtensions
    {
        public static IServiceCollection AddCoreServices(this IServiceCollection services, IConfiguration config)
        {
            services.AddAutoMapper(cfg => { },typeof(ServiceAssemblyReference).Assembly);
            

            services.AddScoped<ISpecialtyService, SpecialtyService>();
            services.AddScoped<IAuthService, AuthService>();
            services.AddScoped<ITokenService, TokenService>();
            services.AddScoped<IDoctorService, DoctorService>();
            services.AddScoped<IPatientService, PatientService>();
            services.AddScoped<IAppointmentBookingService, AppointmentBookingService>();
            services.AddScoped<IAppointmentCancellationService, AppointmentCancellationService>();
            services.AddScoped<IAppointmentVisitService, AppointmentVisitService>();
            services.AddScoped<IReviewService, ReviewService>();
            services.AddScoped<INotificationService, NotificationService>();
            services.AddScoped<IAdminService, AdminService>();
            services.AddScoped<IProfileService, ProfileService>();
            services.AddSingleton(TimeProvider.System);
            services.AddSingleton<IClock, Clock>();
            services.AddScoped<IAccessGuard, AccessGuard>(); // needs ICurrentUser, registered by the host (Presentation implements it)

            // An unknown zone would make every slot-time rule wrong, so fail at startup (REVIEW.md M2)
            services.AddOptions<ClinicSettings>()
                .Bind(config.GetSection("Clinic"))
                .Validate(s => TimeZoneInfo.TryFindSystemTimeZoneById(s.TimeZoneId, out _),
                    "Clinic:TimeZoneId must be a known IANA time zone id, e.g. Africa/Cairo.")
                .ValidateOnStart();


            return services;
        }
    }
}
