using Microsoft.Extensions.Options;
using Domain.Entities.IdentityModule;
using Domain.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Services.Abstraction.Contracts;
using Persistence;
using Persistence.AppData;
using Persistence.Queries;
using Persistence.Tenancy;
using Presentation.Authorization;
using Shared.Settings;
using System.Text;

namespace ClinicFlow.Extensions
{
    public static class InfraStructureServiceExtensions
    {
        public static IServiceCollection AddInfraStructureServices(this IServiceCollection services, IConfiguration config)
        {
            var connectionString = config.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

            // V1 and V2 share the git-ignored appsettings.Development.json, which points at V1's database. Fail
            // before anything (startup migrations, `dotnet ef`) can touch it (docs/v2/adr/0007-v2-separate-product-line.md)
            var databaseSettings = config.GetSection("Database").Get<DatabaseSettings>() ?? new DatabaseSettings();
            if (!databaseSettings.Allows(connectionString))
                throw new InvalidOperationException(
                    $"ConnectionStrings:DefaultConnection points at database '{DatabaseSettings.DatabaseName(connectionString) ?? "(none)"}', " +
                    $"but V2 must use '{databaseSettings.ExpectedName}' (Database:ExpectedName). V2 never runs against V1's database. " +
                    "Set the V2 connection string in user secrets or the environment.");

            // One organization in V2; ClinicDbContext filters and stamps every organization-owned row with it (docs/v2/adr/0001)
            services.AddSingleton<ITenantContext, SingleOrganizationTenantContext>();

            // Transient SQL errors (deadlocks, dropped connections, failover) are retried instead of reaching the user
            // (REVIEW.md NL14). Manual transactions therefore run through IUnitOfWork.ExecuteInTransactionAsync
            services.AddDbContext<ClinicDbContext>(options =>
            {
                options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure());
            });

            services.AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
            {
                options.Password.RequiredLength = 8;
                options.Password.RequireNonAlphanumeric = false;
                options.User.RequireUniqueEmail = true;

                // Brute-force protection: AuthService.LoginAsync records failures with AccessFailedAsync
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.AllowedForNewUsers = true;
            })
            .AddEntityFrameworkStores<ClinicDbContext>()
            .AddDefaultTokenProviders();


            // Fail at startup with a clear message instead of at the first login (IDX10720 → 500)
            services.AddOptions<JwtSettings>()
                .Bind(config.GetSection("JwtSettings"))
                .Validate(s => Encoding.UTF8.GetByteCount(s.Secret) >= 32,
                    "JwtSettings:Secret must be at least 32 bytes (HMAC-SHA256). Set it in user secrets or the environment.")
                .Validate(s => !string.IsNullOrWhiteSpace(s.Issuer) && !string.IsNullOrWhiteSpace(s.Audience),
                    "JwtSettings:Issuer and JwtSettings:Audience are required.")
                .Validate(s => s.AccessTokenExpirationMinutes > 0 && s.RefreshTokenExpirationDays > 0,
                    "JwtSettings token lifetimes must be positive.")
                .ValidateOnStart();

            services.AddAuthentication(opt =>
            {
                opt.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                opt.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
                .AddJwtBearer();

            // Configure the bearer from the validated JwtSettings rather than reading the section eagerly
            services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
                .Configure<IOptions<JwtSettings>>((opt, jwtOptions) =>
                {
                    var jwtSettings = jwtOptions.Value;
                    opt.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidateAudience = true,
                        ValidateLifetime = true,
                        ValidateIssuerSigningKey = true,
                        ValidIssuer = jwtSettings.Issuer,
                        ValidAudience = jwtSettings.Audience,
                        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Secret)),
                        ClockSkew = TimeSpan.Zero, // to prevent token expiration issues due to clock differences
                    };
                });
            // Secure by default: an endpoint without [Authorize] or [AllowAnonymous] requires a logged-in user,
            // so forgetting an attribute can't open an endpoint (REVIEW.md NM5, C1)
            services.AddAuthorization(options =>
            {
                options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
            });
            // [HasPermission] policies are built on demand and checked against the role matrix (docs/v2/adr/0002) and
            // the organization's modules (docs/v2/adr/0008 decision 8): scoped, since IEntitlements is per request.
            // A permission refused for its module answers 403 Subscription.ModuleNotEnabled
            services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
            services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
            services.AddSingleton<IAuthorizationMiddlewareResultHandler, SubscriptionAuthorizationResultHandler>();
            services.AddHttpContextAccessor();
            services.AddScoped<ICurrentUser, HttpCurrentUser>();

            services.AddScoped<IUnitOfWork, UnitOfWork>();

            // Query classes (CQRS-lite) read through the context directly, so unlike repositories they are registered here
            services.AddScoped<IAppointmentQueries, AppointmentQueries>();
            services.AddScoped<IEntitlementQueries, EntitlementQueries>(); // read only by IEntitlements (docs/v2/adr/0008)
            services.AddScoped<ISubscriptionQueries, SubscriptionQueries>();
            services.AddScoped<IOrganizationQueries, OrganizationQueries>();


            return services;
        }
    }
}
