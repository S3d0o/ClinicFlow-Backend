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

            // Every outbox event handler in this assembly (docs/v2/06 §5), like the AutoMapper profiles above
            foreach (var type in typeof(ServiceAssemblyReference).Assembly.GetTypes().Where(t => t is { IsClass: true, IsAbstract: false }))
                foreach (var handler in type.GetInterfaces().Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDomainEventHandler<>)))
                    services.AddScoped(handler, type);
            

            services.AddScoped<ISpecialtyService, SpecialtyService>();
            services.AddScoped<IBranchService, BranchService>();
            services.AddScoped<IBranchCalendarService, BranchCalendarService>();
            services.AddScoped<IOrganizationService, OrganizationService>();
            // Settings (FR-ORG-05): one cache for the process; one service reads (cached) and changes them
            services.AddSingleton<OrganizationSettingsCache>();
            services.AddScoped<OrganizationSettingsService>();
            services.AddScoped<IOrganizationSettings>(sp => sp.GetRequiredService<OrganizationSettingsService>());
            services.AddScoped<IOrganizationSettingsService>(sp => sp.GetRequiredService<OrganizationSettingsService>());
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

            // Modules and limits per organization (docs/v2/adr/0008): one cache for the process, which Persistence
            // invalidates on save; IEntitlementQueries is registered by the host (Persistence implements it)
            services.AddSingleton<EntitlementCache>();
            services.AddSingleton<IEntitlementCache>(sp => sp.GetRequiredService<EntitlementCache>());
            services.AddScoped<IEntitlements, Entitlements>();
            services.AddScoped<ISubscriptionService, SubscriptionService>(); // reads PlatformSettings, configured by the host

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
