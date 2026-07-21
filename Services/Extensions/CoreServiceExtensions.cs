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
            services.AddScoped<IEmailService,EmailService>();
            services.AddScoped<IAuthService, AuthService>();
            services.AddScoped<ITokenService, TokenService>();
            services.AddScoped<IDoctorService, DoctorService>();
            services.AddScoped<IPatientService, PatientService>();
            services.AddScoped<IAppointmentService, AppointmentService>();
            services.AddScoped<IReviewService, ReviewService>();
            services.AddScoped<INotificationService, NotificationService>();
            services.AddScoped<IAdminService, AdminService>();
            services.AddScoped<IProfileService, ProfileService>();
            services.AddSingleton<IDateTimeProvider, DateTimeProvider>();

            services.Configure<EmailSettings>(config.GetSection("EmailSettings"));


            return services;
        }
    }
}
