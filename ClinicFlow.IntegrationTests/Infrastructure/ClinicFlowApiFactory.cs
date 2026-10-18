using ClinicFlow.BackgroundJobs;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Persistence.AppData;
using Services.Abstraction.Contracts;
using Shared.Settings;
using Testcontainers.MsSql;

namespace ClinicFlow.IntegrationTests.Infrastructure
{
    public class ClinicFlowApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
    {
        private readonly MsSqlContainer _dbContainer = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
            .WithPassword("Test@Password123!")
            .Build();

        public async Task InitializeAsync()
        {
            await _dbContainer.StartAsync();
        }

        // Explicit implementation: a public DisposeAsync() would hide WebApplicationFactory.DisposeAsync()
        async Task IAsyncLifetime.DisposeAsync()
        {
            await _dbContainer.DisposeAsync();
            await base.DisposeAsync();
        }

        // Override the ConfigureWebHost method to configure the test server
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Every test class resets the database and seeds its own data, so the large demo clinic
            // (DataSeeder.LargeDemo.cs) would only slow down the test host's startup
            builder.UseSetting("Seeding:DemoData", "false");

            builder.ConfigureServices(services =>
            {
                // 1. remove the existing DbContext registration
                var descriptor = services.SingleOrDefault(
                     d => d.ServiceType == typeof(DbContextOptions<ClinicDbContext>));

                if (descriptor != null)
                {
                    services.Remove(descriptor);
                }

                // 2. Register the test DbContext pointing at the Testcontainers DB
                services.AddDbContext<ClinicDbContext>(opt=>
                opt.UseSqlServer(_dbContainer.GetConnectionString() + ";Database=ClinicFlowTestDb;"));

                // 3. (Optional) Disable hosted services that shouldn't run during tests
                // e.g. background email reminders

                var hostedServiceDescriptors = services.Where(
                    d => d.ServiceType == typeof(IHostedService) &&
                    (d.ImplementationType == typeof(AppointmentReminderJob) || d.ImplementationType == typeof(SlotGenerationJob)))
                    .ToList();

                foreach (var d in hostedServiceDescriptors)
                    services.Remove(d);

                // 4. Never send real email: appsettings.Development.json may hold real SMTP credentials
                foreach (var d in services.Where(d => d.ServiceType == typeof(IEmailService)).ToList())
                    services.Remove(d);

                services.AddSingleton<FakeEmailService>();
                services.AddSingleton<IEmailService>(sp => sp.GetRequiredService<FakeEmailService>());

                // 5. Every test client shares one (unknown) IP, so the auth rate limit would trip across tests.
                //    RateLimitTests lowers it again on its own host
                services.PostConfigure<RateLimitSettings>(s => s.PermitLimit = int.MaxValue);


                // this will ovberride the default authentication scheme to use our TestAuthHandler
                // BUT for cleaner u can first remove the existing authentication scheme and then add the test one

                // Remove existing auth
                var authDescriptors = services
                    .Where(d => d.ServiceType == typeof(IAuthenticationSchemeProvider))
                    .ToList();

                foreach (var d in authDescriptors)
                    services.Remove(d);

                services.AddAuthentication(opt =>
                {
                    opt.DefaultAuthenticateScheme = "Test";
                    opt.DefaultChallengeScheme = "Test";
                }).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { }) ;
            });

            //// Set environment to Testing so you can branch logic if needed
            //builder.UseEnvironment("Testing");
        }

        // Emails "sent" by the API under test
        public FakeEmailService Emails => Services.GetRequiredService<FakeEmailService>();

        // The clinic's wall clock (Clinic:TimeZoneId). Slot dates and times are clinic-local, so seed
        // time-sensitive slots relative to this, not to DateTime.UtcNow (REVIEW.md M2)
        public DateTime ClinicNow => Services.GetRequiredService<IDateTimeProvider>().ClinicNow;
        public DateOnly ClinicToday => DateOnly.FromDateTime(ClinicNow);

        public async Task ResetDataBaseAsync()
        {
            using var scope = Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ClinicDbContext>();
            await dbContext.Database.EnsureDeletedAsync();
            await dbContext.Database.MigrateAsync();

            // Every test class starts with no recorded emails and sending switched back on
            Emails.Clear();
        }

        // Helper: seed data directly into the test DB
        public async Task SeedAsync(Func<ClinicDbContext, Task> seeder)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ClinicDbContext>();
            await seeder(db);
        }
    }
}
