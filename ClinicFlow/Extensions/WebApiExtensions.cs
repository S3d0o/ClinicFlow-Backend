using FluentValidation;
using Presentation.Filters;
using System.Text.Json.Serialization;

namespace ClinicFlow.Extensions
{
    public static class WebApiExtensions
    {
        public static IServiceCollection AddWebApiServices(this IServiceCollection services)
        {
            // Register Web API services, controllers, etc.
            services.AddScoped<ValidationFilter>();
            services.AddControllers(options =>
            {
                options.Filters.AddService<ValidationFilter>();
            })
                .AddApplicationPart(typeof(Presentation.Controllers.ApiController).Assembly)
                .AddJsonOptions(options =>
                {
                    options.JsonSerializerOptions.Converters.Add(
                        new JsonStringEnumConverter());
                });
            services.AddEndpointsApiExplorer();
            services.AddValidatorsFromAssembly(typeof(ValidationFilter).Assembly);

            return services;
        }
    }
}
