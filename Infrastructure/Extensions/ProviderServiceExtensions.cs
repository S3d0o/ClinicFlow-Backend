using Infrastructure.Auth;
using Infrastructure.Email;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shared.Settings;

namespace Infrastructure.Extensions
{
    public static class ProviderServiceExtensions
    {
        // Outside services behind Services.Abstraction interfaces (docs/v2/adr/0006): email now; SMS, payments, storage later
        public static IServiceCollection AddProviderServices(this IServiceCollection services, IConfiguration config)
        {
            services.AddScoped<IEmailService, EmailService>();
            services.AddSingleton<IAccessTokenFactory, JwtAccessTokenFactory>();

            services.Configure<EmailSettings>(config.GetSection("EmailSettings"));
            services.Configure<FrontendSettings>(config.GetSection("Frontend"));

            return services;
        }
    }
}
