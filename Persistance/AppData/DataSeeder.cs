using ClinicFlow.Domain.Enums;
using Domain.Enums;
using Domain.Time;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shared.Enums;

namespace Persistence.AppData;

/// <summary>
/// Seeds the database with realistic Egyptian clinic data.
/// Called from Program.cs on startup. The demo accounts below are only created when
/// Seeding:DemoData is true (default: Development only).
///
/// Seeded accounts (all passwords follow the same pattern):
///   Admin   — admin@clinicflow.com         / Admin@1234
///   Doctors — ahmed.elsayed@clinicflow.com / Doctor@1234   (Cardiology)
///             nour.hassan@clinicflow.com   / Doctor@1234   (Dermatology)
///             karim.mansour@clinicflow.com / Doctor@1234   (Orthopedics)
///             dina.youssef@clinicflow.com  / Doctor@1234   (Pediatrics)
///             tarek.soliman@clinicflow.com / Doctor@1234   (Gastroenterology)
///             layla.naguib@clinicflow.com  / Doctor@1234   (Neurology)
///             omar.farouk@clinicflow.com   / Doctor@1234   (ENT — pending approval)
///   Patients — mohamed.ali@gmail.com       / Patient@1234
///              sara.ibrahim@gmail.com      / Patient@1234
///              hassan.mahmoud@gmail.com    / Patient@1234
///              rana.khalil@gmail.com       / Patient@1234
///              youssef.badawi@gmail.com    / Patient@1234
///
/// On top of these, DataSeeder.LargeDemo.cs generates a large, repeatable clinic for load-style testing:
///   doctor01…doctor53@clinicflow.test   / Doctor@1234   (60 doctors in total; 5 pending, 3 suspended)
///   patient01…patient75@clinicflow.test / Patient@1234  (80 patients in total)
/// plus about 2,500 appointments over 60 days of history and 21 days ahead, reviews and notifications.
/// </summary>
public static partial class DataSeeder
{
    // ── Fixed GUIDs ───────────────────────────────────────────────────────────
    // Using fixed IDs makes the seeder idempotent: re-running never duplicates rows.

    private static readonly Guid AdminId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    // Doctors
    private static readonly Guid DrAhmedId = Guid.Parse("00000000-0000-0000-0000-000000000010");
    private static readonly Guid DrNourId = Guid.Parse("00000000-0000-0000-0000-000000000011");
    private static readonly Guid DrKarimId = Guid.Parse("00000000-0000-0000-0000-000000000012");
    private static readonly Guid DrDinaId = Guid.Parse("00000000-0000-0000-0000-000000000013");
    private static readonly Guid DrTarekId = Guid.Parse("00000000-0000-0000-0000-000000000015");
    private static readonly Guid DrLaylaId = Guid.Parse("00000000-0000-0000-0000-000000000016");
    private static readonly Guid DrOmarId = Guid.Parse("00000000-0000-0000-0000-000000000014"); // pending

    // Patients
    private static readonly Guid PatMohamedId = Guid.Parse("00000000-0000-0000-0000-000000000020");
    private static readonly Guid PatSaraId = Guid.Parse("00000000-0000-0000-0000-000000000021");
    private static readonly Guid PatHassanId = Guid.Parse("00000000-0000-0000-0000-000000000022");
    private static readonly Guid PatRanaId = Guid.Parse("00000000-0000-0000-0000-000000000023");
    private static readonly Guid PatYoussefId = Guid.Parse("00000000-0000-0000-0000-000000000024");

    // Demo doctors work Sun–Thu (the Egyptian working week)
    private static readonly DayOfWeek[] WorkDays =
        [DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday];

    // Demo slots cover this window around today; it is topped up on every start (see SeedAppointmentSlotsAsync).
    // 60 days of history gives the generated clinic enough past visits for statistics and paging.
    private const int SlotHistoryDays = 60;
    private const int SlotBookingWindowDays = 21;

    // Slot dates and times are clinic-local wall-clock values; stored timestamps (BookedAt, CancelledAt,
    // CreatedAt, …) are UTC. Seeding with UTC dates put demo data a day off around midnight (REVIEW.md M2).
    // The zone is the clinic's (= the MAIN branch's), so conversions follow its daylight saving rules, as slot generation does.
    private sealed record SeedClock(DateTime ClinicNow, DateTime UtcNow, TimeZoneInfo Zone)
    {
        public DateOnly Today => DateOnly.FromDateTime(ClinicNow);

        // A slot's instants; null when daylight saving skips the time (Domain.Time.WallClock)
        public DateTime? SlotUtc(DateOnly date, TimeOnly time) => WallClock.ToUtc(Zone, date, time);

        // For timestamps derived from a local time; one skipped by daylight saving moves to the end of the jump
        public DateTime ToUtc(DateTime clinicLocal)
            => SlotUtc(DateOnly.FromDateTime(clinicLocal), TimeOnly.FromDateTime(clinicLocal)) ?? ToUtc(clinicLocal.AddHours(1));
    }

    // ── Entry point ───────────────────────────────────────────────────────────
    // Roles and specialties are always seeded. The demo dataset (accounts with the published
    // passwords above) is only seeded when seedDemoData is true — never enable it in production.
    // A real admin can be bootstrapped from configuration (Seeding:AdminEmail / Seeding:AdminPassword).
    // clinicNow and utcNow come from IClock, clinicTimeZoneId from Clinic:TimeZoneId (Persistence can't reference either).
    // migrateOnStartup is Database:MigrateOnStartup (EnsureSchemaAsync).
    public static async Task SeedAsync(
        IServiceProvider service, bool migrateOnStartup, bool seedDemoData, DateTime clinicNow, DateTime utcNow, string clinicTimeZoneId,
        string? adminEmail = null, string? adminPassword = null)
    {
        using var scope = service.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<ClinicDbContext>();
        // Seeded history is not news, nor an action anyone took: its transitions must not notify or email anyone
        // (docs/v2/06 §5) or write audit entries (FR-AUD-01), which would also slow the large demo seed
        context.RecordDomainEvents = false;
        context.RecordAudit = false;
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DataSeeder));

        await EnsureSchemaAsync(context, migrateOnStartup, logger);

        await SyncMainBranchTimeZoneAsync(context, clinicTimeZoneId);
        await SeedRolesAsync(roleManager);
        await SeedSpecialtiesAsync(context);

        if (!string.IsNullOrWhiteSpace(adminEmail) && !string.IsNullOrWhiteSpace(adminPassword))
            await SeedBootstrapAdminAsync(userManager, adminEmail, adminPassword, utcNow);

        if (!seedDemoData)
            return;

        var clock = new SeedClock(clinicNow, utcNow, TimeZoneInfo.FindSystemTimeZoneById(clinicTimeZoneId));

        // The hand-written demo stories (the accounts listed above) first, then the generated clinic.
        // Each step only runs if its data is missing, so restarts don't duplicate anything.
        await SeedUsersAsync(userManager, context, clock);
        await SeedDoctorSchedulesAsync(context);
        await SeedGeneratedPeopleAsync(context, clock, logger);
        await SeedAppointmentSlotsAsync(context, clock);
        await SeedAppointmentsAsync(context, clock);
        await SeedReviewsAsync(context, clock);
        await SeedNotificationsAsync(context, clock);
        await SeedGeneratedActivityAsync(context, clock, logger);
        await SeedDemoSubscriptionPeriodAsync(context, clock);
    }

    // The migration puts the organization on the Full plan with recruitment added and no billing period (subscription
    // billing comes in R4). The "Plan & modules" screen shows when the period ends, so the demo gets one 30 days
    // ahead, moved forward on any start once it has passed, as the demo slots are
    private static async Task SeedDemoSubscriptionPeriodAsync(ClinicDbContext context, SeedClock clock)
    {
        var subscription = await context.OrganizationSubscriptions.SingleOrDefaultAsync();
        if (subscription is null || subscription.CurrentPeriodEnd > clock.UtcNow)
            return;

        subscription.CurrentPeriodEnd = clock.UtcNow.AddDays(30);
        await context.SaveChangesAsync();
    }

    // Brings the schema up to date (migrate = true: `dotnet run` and the tests), or checks that it already is
    // (Docker and production, where the migration bundle runs first as a deploy step: docs/v2/plans/P0.md D1,
    // REVIEW.md §4.9). Starting against an older schema would fail on the first query that needs a new column,
    // so it fails here instead, naming the pending migrations. The app's own login can't run DDL there anyway.
    public static async Task EnsureSchemaAsync(ClinicDbContext context, bool migrate, ILogger logger)
    {
        if (migrate)
        {
            await WithRetryWhileDatabaseRecoversAsync(() => context.Database.MigrateAsync(), logger);
            return;
        }

        var pending = (await WithRetryWhileDatabaseRecoversAsync(() => context.Database.GetPendingMigrationsAsync(), logger)).ToList();
        if (pending.Count > 0)
            throw new InvalidOperationException(
                $"The database is missing {pending.Count} migration(s): {string.Join(", ", pending)}. " +
                "Run the migration bundle (the migrate service in docker-compose.yml) before starting the app, " +
                "or set Database:MigrateOnStartup to true in development.");
    }

    // SQL Server accepts connections before it has finished recovering existing databases (e.g. when
    // the db and backend containers start together). In that window EF reads "cannot open database"
    // as "database doesn't exist", runs CREATE DATABASE and fails with error 1801. Retry until it's online.
    private static async Task<T> WithRetryWhileDatabaseRecoversAsync<T>(Func<Task<T>> operation, ILogger logger)
    {
        const int maxAttempts = 12;
        var delay = TimeSpan.FromSeconds(5);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await operation();
            }
            catch (SqlException ex) when (attempt < maxAttempts)
            {
                logger.LogWarning(
                    "Database not ready (SQL error {SqlErrorNumber}: {SqlErrorMessage}). Retrying in {DelaySeconds}s (attempt {Attempt}/{MaxAttempts})",
                    ex.Number, ex.Message, delay.TotalSeconds, attempt, maxAttempts);
                await Task.Delay(delay);
            }
        }
    }

    private static Task WithRetryWhileDatabaseRecoversAsync(Func<Task> operation, ILogger logger)
        => WithRetryWhileDatabaseRecoversAsync(async () =>
        {
            await operation();
            return true;
        }, logger);

    // ── 0. Organization and main branch ──────────────────────────────────────
    // The migration creates both with Africa/Cairo; the configured clinic zone wins, so a deployment
    // elsewhere gets the right zone once branch time zones drive slot times (P0 PR 6, docs/v2/adr/0003)
    private static async Task SyncMainBranchTimeZoneAsync(ClinicDbContext context, string clinicTimeZoneId)
    {
        var organization = await context.Organizations.SingleAsync(o => o.Id == Organization.DefaultId);
        var branch = await context.Branches.SingleAsync(b => b.Id == Branch.MainId);
        if (organization.DefaultTimeZoneId == clinicTimeZoneId && branch.TimeZoneId == clinicTimeZoneId)
            return;

        organization.DefaultTimeZoneId = clinicTimeZoneId;
        branch.TimeZoneId = clinicTimeZoneId;
        await context.SaveChangesAsync();
    }

    // ── 1. Roles ──────────────────────────────────────────────────────────────
    private static async Task SeedRolesAsync(RoleManager<IdentityRole<Guid>> roleManager)
    {
        foreach (var role in new[] { Roles.Admin, Roles.Doctor, Roles.Patient })
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole<Guid>(role));
        }
    }

    // ── 2. Specialties ────────────────────────────────────────────────────────
    private static async Task SeedSpecialtiesAsync(ClinicDbContext context)
    {
        if (await context.Specialties.AnyAsync()) return;

        var specialties = new List<Specialty>
        {
            new() { Name = "Cardiology",        Description = "Heart and cardiovascular system diseases",              IsActive = true },
            new() { Name = "Dermatology",        Description = "Skin, hair, and nail conditions",                      IsActive = true },
            new() { Name = "Orthopedics",        Description = "Bones, joints, and musculoskeletal system",            IsActive = true },
            new() { Name = "Pediatrics",         Description = "Medical care for infants, children, and adolescents",  IsActive = true },
            new() { Name = "Neurology",          Description = "Nervous system and brain disorders",                   IsActive = true },
            new() { Name = "Gynecology",         Description = "Female reproductive health",                           IsActive = true },
            new() { Name = "Ophthalmology",      Description = "Eye diseases and vision care",                         IsActive = true },
            new() { Name = "ENT",               Description = "Ear, Nose and Throat disorders",                       IsActive = true },
            new() { Name = "Gastroenterology",  Description = "Digestive system and gastrointestinal tract",           IsActive = true },
            new() { Name = "Psychiatry",         Description = "Mental health and behavioral disorders",               IsActive = true },
        };

        await context.Specialties.AddRangeAsync(specialties);
        await context.SaveChangesAsync();
    }

    // ── 3. Users + Profiles ───────────────────────────────────────────────────
    private static async Task SeedUsersAsync(UserManager<ApplicationUser> userManager, ClinicDbContext context, SeedClock clock)
    {
        if (await context.DoctorProfiles.AnyAsync()) return;

        var specialties = await context.Specialties.ToListAsync();
        Specialty Specialty(string name) => specialties.First(s => s.Name == name);

        // ── Admin ─────────────────────────────────────────────────────────────
        await CreateUserAsync(userManager, new ApplicationUser
        {
            Id = AdminId,
            FirstName = "Saad",
            LastName = "Admin",
            Email = "admin@clinicflow.com",
            UserName = "admin@clinicflow.com",
            Gender = Gender.Male,
            DateOfBirth = new DateOnly(1990, 1, 1),
            CreatedAt = clock.UtcNow,
            IsActive = true,
            EmailConfirmed = true,
        }, "Admin@1234", Roles.Admin);

        // ── Doctors ───────────────────────────────────────────────────────────
        var doctors = new[]
        {
            (User: new ApplicationUser
            {
                Id             = DrAhmedId,
                FirstName      = "Ahmed",
                LastName       = "El-Sayed",
                Email          = "ahmed.elsayed@clinicflow.com",
                UserName       = "ahmed.elsayed@clinicflow.com",
                PhoneNumber    = "+201001110001",
                Gender         = Gender.Male,
                DateOfBirth    = new DateOnly(1974, 4, 15),
                CreatedAt      = clock.UtcNow,
                IsActive       = true,
                EmailConfirmed = true,
            },
            Profile: new DoctorProfile
            {
                UserId            = DrAhmedId,
                SpecialtyId       = Specialty("Cardiology").Id,
                Bio               = "Senior interventional cardiologist with 25+ years at Cairo University Hospital. Specializes in coronary artery disease, heart failure, and echocardiography.",
                YearsOfExperience = 25,
                ConsultationFee   = 500,
                IsApprovedByAdmin = true,
                ClinicAddress     = "15 Tahrir Square, Building 3, Floor 2",
                ClinicCity        = "Cairo",
            }),

            (User: new ApplicationUser
            {
                Id             = DrNourId,
                FirstName      = "Nour",
                LastName       = "Hassan",
                Email          = "nour.hassan@clinicflow.com",
                UserName       = "nour.hassan@clinicflow.com",
                PhoneNumber    = "+201001110002",
                Gender         = Gender.Female,
                DateOfBirth    = new DateOnly(1985, 9, 22),
                CreatedAt      = clock.UtcNow,
                IsActive       = true,
                EmailConfirmed = true,
            },
            Profile: new DoctorProfile
            {
                UserId            = DrNourId,
                SpecialtyId       = Specialty("Dermatology").Id,
                Bio               = "Cosmetic and medical dermatologist trained in Cairo and Paris. Expert in laser therapy, acne management, and skin cancer screening.",
                YearsOfExperience = 12,
                ConsultationFee   = 400,
                IsApprovedByAdmin = true,
                ClinicAddress     = "7 Mohamed Farid Street, Maadi",
                ClinicCity        = "Cairo",
            }),

            (User: new ApplicationUser
            {
                Id             = DrKarimId,
                FirstName      = "Karim",
                LastName       = "Mansour",
                Email          = "karim.mansour@clinicflow.com",
                UserName       = "karim.mansour@clinicflow.com",
                PhoneNumber    = "+201001110003",
                Gender         = Gender.Male,
                DateOfBirth    = new DateOnly(1979, 3, 10),
                CreatedAt      = clock.UtcNow,
                IsActive       = true,
                EmailConfirmed = true,
            },
            Profile: new DoctorProfile
            {
                UserId            = DrKarimId,
                SpecialtyId       = Specialty("Orthopedics").Id,
                Bio               = "Orthopedic surgeon specializing in sports injuries, ACL reconstruction, and total joint replacement. Fellow of the Egyptian Orthopedic Association.",
                YearsOfExperience = 18,
                ConsultationFee   = 600,
                IsApprovedByAdmin = true,
                ClinicAddress     = "22 Gesr El Suez Street, Heliopolis",
                ClinicCity        = "Cairo",
            }),

            (User: new ApplicationUser
            {
                Id             = DrDinaId,
                FirstName      = "Dina",
                LastName       = "Youssef",
                Email          = "dina.youssef@clinicflow.com",
                UserName       = "dina.youssef@clinicflow.com",
                PhoneNumber    = "+201001110004",
                Gender         = Gender.Female,
                DateOfBirth    = new DateOnly(1982, 7, 30),
                CreatedAt      = clock.UtcNow,
                IsActive       = true,
                EmailConfirmed = true,
            },
            Profile: new DoctorProfile
            {
                UserId            = DrDinaId,
                SpecialtyId       = Specialty("Pediatrics").Id,
                Bio               = "Pediatrician focused on neonatal care, childhood development, and vaccination programs. 20 years serving families across Alexandria.",
                YearsOfExperience = 20,
                ConsultationFee   = 350,
                IsApprovedByAdmin = true,
                ClinicAddress     = "5 El-Horreya Road, Smouha",
                ClinicCity        = "Alexandria",
            }),

            (User: new ApplicationUser
            {
                Id             = DrTarekId,
                FirstName      = "Tarek",
                LastName       = "Soliman",
                Email          = "tarek.soliman@clinicflow.com",
                UserName       = "tarek.soliman@clinicflow.com",
                PhoneNumber    = "+201001110005",
                Gender         = Gender.Male,
                DateOfBirth    = new DateOnly(1977, 11, 18),
                CreatedAt      = clock.UtcNow,
                IsActive       = true,
                EmailConfirmed = true,
            },
            Profile: new DoctorProfile
            {
                UserId            = DrTarekId,
                SpecialtyId       = Specialty("Gastroenterology").Id,
                Bio               = "Gastroenterologist with expertise in colonoscopy, GERD management, IBD, and liver disease. Trained at Ain Shams University.",
                YearsOfExperience = 16,
                ConsultationFee   = 450,
                IsApprovedByAdmin = true,
                ClinicAddress     = "3 Cleopatra Street, Heliopolis",
                ClinicCity        = "Cairo",
            }),

            (User: new ApplicationUser
            {
                Id             = DrLaylaId,
                FirstName      = "Layla",
                LastName       = "Naguib",
                Email          = "layla.naguib@clinicflow.com",
                UserName       = "layla.naguib@clinicflow.com",
                PhoneNumber    = "+201001110006",
                Gender         = Gender.Female,
                DateOfBirth    = new DateOnly(1983, 2, 5),
                CreatedAt      = clock.UtcNow,
                IsActive       = true,
                EmailConfirmed = true,
            },
            Profile: new DoctorProfile
            {
                UserId            = DrLaylaId,
                SpecialtyId       = Specialty("Neurology").Id,
                Bio               = "Neurologist specializing in epilepsy, migraine disorders, and multiple sclerosis. Board-certified with 14 years of clinical practice.",
                YearsOfExperience = 14,
                ConsultationFee   = 550,
                IsApprovedByAdmin = true,
                ClinicAddress     = "18 El-Nasr Road, Nasr City",
                ClinicCity        = "Cairo",
            }),

            // Pending approval — no schedule, no slots, no appointments
            (User: new ApplicationUser
            {
                Id             = DrOmarId,
                FirstName      = "Omar",
                LastName       = "Farouk",
                Email          = "omar.farouk@clinicflow.com",
                UserName       = "omar.farouk@clinicflow.com",
                PhoneNumber    = "+201001110007",
                Gender         = Gender.Male,
                DateOfBirth    = new DateOnly(1991, 11, 5),
                CreatedAt      = clock.UtcNow,
                IsActive       = true,
                EmailConfirmed = true,
            },
            Profile: new DoctorProfile
            {
                UserId            = DrOmarId,
                SpecialtyId       = Specialty("ENT").Id,
                Bio               = "ENT specialist with experience in rhinoplasty, sinusitis treatment, and cochlear implants.",
                YearsOfExperience = 6,
                ConsultationFee   = 380,
                IsApprovedByAdmin = false,
                ClinicAddress     = "10 Abbas El-Akkad Street, Nasr City",
                ClinicCity        = "Cairo",
            }),
        };

        foreach (var (user, profile) in doctors)
        {
            await CreateUserAsync(userManager, user, "Doctor@1234", Roles.Doctor);
            profile.UserId = user.Id;
            await context.DoctorProfiles.AddAsync(profile);
        }

        // ── Patients ──────────────────────────────────────────────────────────
        var patients = new[]
        {
            (User: new ApplicationUser
            {
                Id             = PatMohamedId,
                FirstName      = "Mohamed",
                LastName       = "Ali",
                Email          = "mohamed.ali@gmail.com",
                UserName       = "mohamed.ali@gmail.com",
                PhoneNumber    = "+201009001001",
                Gender         = Gender.Male,
                DateOfBirth    = new DateOnly(1988, 6, 14),
                CreatedAt      = clock.UtcNow,
                IsActive       = true,
                EmailConfirmed = true,
            },
            Profile: new PatientProfile
            {
                UserId                = PatMohamedId,
                BloodType             = BloodType.OPositive,
                Allergies             = "Penicillin",
                ChronicConditions     = "Hypertension",
                EmergencyContactName  = "Fatma Ali",
                EmergencyContactPhone = "+201001234567",
            }),

            (User: new ApplicationUser
            {
                Id             = PatSaraId,
                FirstName      = "Sara",
                LastName       = "Ibrahim",
                Email          = "sara.ibrahim@gmail.com",
                UserName       = "sara.ibrahim@gmail.com",
                PhoneNumber    = "+201009001002",
                Gender         = Gender.Female,
                DateOfBirth    = new DateOnly(1995, 3, 28),
                CreatedAt      = clock.UtcNow,
                IsActive       = true,
                EmailConfirmed = true,
            },
            Profile: new PatientProfile
            {
                UserId                = PatSaraId,
                BloodType             = BloodType.APositive,
                Allergies             = null,
                ChronicConditions     = "Asthma",
                EmergencyContactName  = "Khaled Ibrahim",
                EmergencyContactPhone = "+201009876543",
            }),

            (User: new ApplicationUser
            {
                Id             = PatHassanId,
                FirstName      = "Hassan",
                LastName       = "Mahmoud",
                Email          = "hassan.mahmoud@gmail.com",
                UserName       = "hassan.mahmoud@gmail.com",
                PhoneNumber    = "+201009001003",
                Gender         = Gender.Male,
                DateOfBirth    = new DateOnly(1982, 12, 1),
                CreatedAt      = clock.UtcNow,
                IsActive       = true,
                EmailConfirmed = true,
            },
            Profile: new PatientProfile
            {
                UserId                = PatHassanId,
                BloodType             = BloodType.BNegative,
                Allergies             = "Sulfa drugs, Latex",
                ChronicConditions     = null,
                EmergencyContactName  = "Amira Mahmoud",
                EmergencyContactPhone = "+201112223344",
            }),

            (User: new ApplicationUser
            {
                Id             = PatRanaId,
                FirstName      = "Rana",
                LastName       = "Khalil",
                Email          = "rana.khalil@gmail.com",
                UserName       = "rana.khalil@gmail.com",
                PhoneNumber    = "+201009001004",
                Gender         = Gender.Female,
                DateOfBirth    = new DateOnly(1998, 8, 17),
                CreatedAt      = clock.UtcNow,
                IsActive       = true,
                EmailConfirmed = true,
            },
            Profile: new PatientProfile
            {
                UserId                = PatRanaId,
                BloodType             = BloodType.ABPositive,
                Allergies             = "Ibuprofen",
                ChronicConditions     = "Migraines",
                EmergencyContactName  = "Sami Khalil",
                EmergencyContactPhone = "+201556667788",
            }),

            (User: new ApplicationUser
            {
                Id             = PatYoussefId,
                FirstName      = "Youssef",
                LastName       = "Badawi",
                Email          = "youssef.badawi@gmail.com",
                UserName       = "youssef.badawi@gmail.com",
                PhoneNumber    = "+201009001005",
                Gender         = Gender.Male,
                DateOfBirth    = new DateOnly(1975, 5, 3),
                CreatedAt      = clock.UtcNow,
                IsActive       = true,
                EmailConfirmed = true,
            },
            Profile: new PatientProfile
            {
                UserId                = PatYoussefId,
                BloodType             = BloodType.ONegative,
                Allergies             = null,
                ChronicConditions     = "Type 2 Diabetes, High Cholesterol",
                EmergencyContactName  = "Nadia Badawi",
                EmergencyContactPhone = "+201223334455",
            }),
        };

        foreach (var (user, profile) in patients)
        {
            await CreateUserAsync(userManager, user, "Patient@1234", Roles.Patient);
            profile.UserId = user.Id;
            await context.PatientProfiles.AddAsync(profile);
        }

        await context.SaveChangesAsync();
    }

    // ── 4. Doctor Schedules ───────────────────────────────────────────────────
    private static async Task SeedDoctorSchedulesAsync(ClinicDbContext context)
    {
        if (await context.DoctorSchedules.AnyAsync()) return;

        // Load only approved doctors — pending doctors have no schedule yet.
        var approvedDoctors = await context.DoctorProfiles
            .Where(d => d.IsApprovedByAdmin)
            .ToListAsync();

        var schedules = new List<DoctorSchedule>();

        foreach (var doctor in approvedDoctors)
        {
            // Each doctor works Sun–Thu, morning shift 09:00–13:00 (30-min slots)
            // and an afternoon shift 16:00–19:00 (30-min slots) to make the UI richer.
            foreach (var day in WorkDays)
            {
                schedules.Add(new DoctorSchedule
                {
                    DoctorProfileId = doctor.Id,
                    BranchId = Branch.MainId,
                    DayOfWeek = day,
                    StartTime = new TimeOnly(9, 0),
                    EndTime = new TimeOnly(13, 0),
                    SlotDurationMinutes = 30,
                    IsActive = true,
                });

                schedules.Add(new DoctorSchedule
                {
                    DoctorProfileId = doctor.Id,
                    BranchId = Branch.MainId,
                    DayOfWeek = day,
                    StartTime = new TimeOnly(16, 0),
                    EndTime = new TimeOnly(19, 0),
                    SlotDurationMinutes = 30,
                    IsActive = true,
                });
            }
        }

        await context.DoctorSchedules.AddRangeAsync(schedules);
        await context.SaveChangesAsync();
    }

    // ── 5. Appointment Slots ──────────────────────────────────────────────────
    // Runs on every start, not just the first: it adds any missing slots from SlotHistoryDays ago to
    // SlotBookingWindowDays ahead, so a long-lived demo database keeps bookable slots. Existing slots
    // (available, booked or blocked) are never touched.
    private static async Task SeedAppointmentSlotsAsync(ClinicDbContext context, SeedClock clock)
    {
        var today = clock.Today;
        var from = today.AddDays(-SlotHistoryDays);
        var to = today.AddDays(SlotBookingWindowDays);

        var schedules = await context.DoctorSchedules
            .Where(s => s.IsActive && s.SlotDurationMinutes > 0)
            .ToListAsync();

        var existing = (await context.AppointmentSlots
                .Where(s => s.Date >= from && s.Date <= to)
                .Select(s => new { s.DoctorProfileId, s.Date, s.StartTime })
                .ToListAsync())
            .Select(s => (s.DoctorProfileId, s.Date, s.StartTime))
            .ToHashSet();

        var slots = new List<AppointmentSlot>();

        for (var date = from; date <= to; date = date.AddDays(1))
        {
            foreach (var schedule in schedules.Where(s => s.DayOfWeek == date.DayOfWeek))
            {
                // Minutes since midnight, as in DoctorService: TimeOnly.AddMinutes wraps at midnight
                var duration = schedule.SlotDurationMinutes;
                var startMinutes = (int)schedule.StartTime.ToTimeSpan().TotalMinutes;
                var endMinutes = (int)schedule.EndTime.ToTimeSpan().TotalMinutes;

                for (var minute = startMinutes; minute + duration <= endMinutes; minute += duration)
                {
                    var start = TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(minute));
                    var end = start.AddMinutes(duration);
                    if (clock.SlotUtc(date, start) is not { } startsAtUtc || clock.SlotUtc(date, end) is not { } endsAtUtc) continue;
                    if (!existing.Add((schedule.DoctorProfileId, date, start))) continue;

                    slots.Add(new AppointmentSlot
                    {
                        DoctorProfileId = schedule.DoctorProfileId,
                        DoctorScheduleId = schedule.Id,
                        BranchId = schedule.BranchId,
                        Date = date,
                        StartTime = start,
                        EndTime = end,
                        StartsAtUtc = startsAtUtc,
                        EndsAtUtc = endsAtUtc,
                        Status = SlotStatus.Available,
                    });
                }
            }
        }

        if (slots.Count == 0) return;

        await context.AppointmentSlots.AddRangeAsync(slots);
        await context.SaveChangesAsync();
    }

    // ── 6. Appointments ───────────────────────────────────────────────────────
    // Status flow (Pending removed — booking creates Confirmed directly):
    //   Confirmed  → can be Cancelled by Patient / Doctor / Admin, or marked Completed
    //   Completed  → eligible for a Review
    //   Cancelled  → slot freed back to Available
    //   NoShow     → patient did not attend a past Confirmed appointment
    //
    // Dates are counted in working days from today (see WorkDay), so every appointment lands on a day
    // the doctor works, whatever weekday the database is seeded on. Each (patient, doctor, status)
    // combination is used once, which is how SeedReviewsAsync finds the visit a review belongs to.
    private static async Task SeedAppointmentsAsync(ClinicDbContext context, SeedClock clock)
    {
        if (await context.Appointments.AnyAsync()) return;

        var now = clock.UtcNow;
        var today = clock.Today;
        var firstSlotDate = today.AddDays(-SlotHistoryDays);

        var doctorIds = await context.DoctorProfiles.ToDictionaryAsync(d => d.UserId, d => d.Id);
        var patientIds = await context.PatientProfiles.ToDictionaryAsync(p => p.UserId, p => p.Id);
        var slots = await context.AppointmentSlots
            .Where(s => s.Status == SlotStatus.Available && s.Date >= firstSlotDate)
            .ToListAsync();

        var appointments = new List<Appointment>();

        void Add(Guid doctorUserId, Guid patientUserId, int workDayOffset, TimeOnly time,
            AppointmentStatus status, string reason, string? notes = null, int bookedDaysBefore = 2,
            (CancelledBy By, string Reason)? cancellation = null)
        {
            if (!doctorIds.TryGetValue(doctorUserId, out var doctorProfileId) ||
                !patientIds.TryGetValue(patientUserId, out var patientProfileId)) return;

            var date = WorkDay(today, workDayOffset);
            var slot = slots.FirstOrDefault(s =>
                s.DoctorProfileId == doctorProfileId && s.Date == date && s.StartTime == time);
            if (slot is null || slot.Status != SlotStatus.Available) return;

            var start = clock.ToUtc(date.ToDateTime(time));
            var appointment = Book(slot, patientProfileId, reason, (start < now ? start : now).AddDays(-bookedDaysBefore));

            if (cancellation is { } c)
            {
                var cancelledAt = appointment.BookedAt.AddDays(1);
                Apply(appointment, appointment.Cancel(c.By, cancelledAt < now ? cancelledAt : now, c.Reason));
                // A cancelled appointment frees its slot, as the cancellation service does
            }
            else
            {
                ApplyHistory(appointment, status, notes, clock);
                slot.Status = SlotStatus.Booked;
            }

            appointments.Add(appointment);
        }

        // ── Past: Completed ──────────────────────────────────────────────────
        Add(DrAhmedId, PatMohamedId, -1, new TimeOnly(9, 0), AppointmentStatus.Completed,
            "Chest pain and shortness of breath during exercise.",
            "Mild hypertension detected. ECG normal. Prescribed Amlodipine 5 mg once daily. Advised low-sodium diet. Follow-up in 4 weeks.",
            bookedDaysBefore: 3);

        Add(DrNourId, PatSaraId, -2, new TimeOnly(9, 30), AppointmentStatus.Completed,
            "Persistent skin rash on forearms and neck for three weeks.",
            "Contact dermatitis, likely triggered by new detergent. Prescribed Hydrocortisone 1% cream twice daily for 7 days. Avoid irritants.",
            bookedDaysBefore: 3);

        Add(DrKarimId, PatHassanId, -3, new TimeOnly(10, 0), AppointmentStatus.Completed,
            "Right knee pain and swelling after football match.",
            "MRI ordered — suspected partial ACL tear. Advised RICE protocol and physiotherapy 3x/week. NSAIDs for pain management. Follow-up in 2 weeks.",
            bookedDaysBefore: 1);

        Add(DrLaylaId, PatRanaId, -4, new TimeOnly(9, 0), AppointmentStatus.Completed,
            "Severe recurring migraines, 3–4 episodes per week.",
            "Chronic migraine confirmed. Started on Topiramate 25 mg nightly as prophylaxis. Sumatriptan 50 mg for acute attacks. Keep headache diary.",
            bookedDaysBefore: 3);

        // No doctor notes yet on the next three, so the "add notes" flow can be demoed
        Add(DrTarekId, PatYoussefId, -3, new TimeOnly(10, 0), AppointmentStatus.Completed,
            "Recurring acid reflux and upper abdominal discomfort after meals.", bookedDaysBefore: 3);

        Add(DrDinaId, PatMohamedId, -2, new TimeOnly(9, 0), AppointmentStatus.Completed,
            "Child's 18-month routine checkup and vaccination.", bookedDaysBefore: 3);

        Add(DrAhmedId, PatSaraId, -2, new TimeOnly(11, 0), AppointmentStatus.Completed, // Sara sees Dr. Nour at 09:30 that day
            "Palpitations and irregular heartbeat noticed for two days.");

        // ── Past: Confirmed but not yet completed (Dr. Ahmed can mark it Completed) ──
        Add(DrAhmedId, PatHassanId, -1, new TimeOnly(16, 0), AppointmentStatus.Confirmed,
            "Routine cardiac checkup and blood pressure review.");

        // ── Past: NoShow ─────────────────────────────────────────────────────
        Add(DrKarimId, PatYoussefId, -5, new TimeOnly(9, 0), AppointmentStatus.NoShow,
            "Right shoulder impingement follow-up.", bookedDaysBefore: 3);

        // ── Upcoming: Cancelled ──────────────────────────────────────────────
        Add(DrNourId, PatRanaId, 4, new TimeOnly(10, 0), AppointmentStatus.Cancelled,
            "Acne treatment consultation.", bookedDaysBefore: 3,
            cancellation: (CancelledBy.Patient, "Personal schedule conflict — will rebook next week."));

        Add(DrTarekId, PatHassanId, 5, new TimeOnly(16, 0), AppointmentStatus.Cancelled,
            "Follow-up after colonoscopy results.", bookedDaysBefore: 4,
            cancellation: (CancelledBy.Doctor, "Doctor unavailable due to emergency surgery. Slot rescheduled."));

        // ── Upcoming: Confirmed ──────────────────────────────────────────────
        Add(DrAhmedId, PatMohamedId, 1, new TimeOnly(9, 0), AppointmentStatus.Confirmed,
            "Follow-up on Amlodipine — blood pressure monitoring.");

        Add(DrNourId, PatSaraId, 2, new TimeOnly(10, 0), AppointmentStatus.Confirmed,
            "Laser session for pigmentation on cheeks.", bookedDaysBefore: 1);

        Add(DrKarimId, PatHassanId, 3, new TimeOnly(16, 0), AppointmentStatus.Confirmed,
            "MRI results review and ACL treatment plan.", bookedDaysBefore: 1);

        Add(DrLaylaId, PatRanaId, 5, new TimeOnly(9, 0), AppointmentStatus.Confirmed,
            "Migraine follow-up — Topiramate dosage check.", bookedDaysBefore: 1);

        Add(DrTarekId, PatYoussefId, 6, new TimeOnly(16, 30), AppointmentStatus.Confirmed,
            "GERD follow-up — 4-week Omeprazole check.", bookedDaysBefore: 1);

        Add(DrDinaId, PatMohamedId, 8, new TimeOnly(9, 30), AppointmentStatus.Confirmed,
            "Child fever and ear pain for two days.", bookedDaysBefore: 1);

        Add(DrLaylaId, PatSaraId, 10, new TimeOnly(10, 0), AppointmentStatus.Confirmed,
            "Frequent numbness in left arm — concerned about nerve issues.", bookedDaysBefore: 1);

        await context.Appointments.AddRangeAsync(appointments);
        await context.SaveChangesAsync();
    }

    // ── 7. Reviews ────────────────────────────────────────────────────────────
    // Sara's completed visit with Dr. Ahmed is left unreviewed so the "leave a review" flow can be
    // demoed. Doctor ratings are then derived from the seeded reviews, as ReviewService does.
    private static async Task SeedReviewsAsync(ClinicDbContext context, SeedClock clock)
    {
        if (await context.Reviews.AnyAsync()) return;

        var appointments = await LoadAppointmentsAsync(context);
        var now = clock.UtcNow;

        var reviewData = new[]
        {
            (Patient: PatMohamedId, Doctor: DrAhmedId, Rating: 5, Comment: "Dr. Ahmed is exceptional. He explained my condition in detail and the treatment is already making a difference. Highly recommend."),
            (Patient: PatSaraId, Doctor: DrNourId, Rating: 5, Comment: "Dr. Nour was very thorough and kind. The cream she prescribed cleared the rash completely within a week."),
            (Patient: PatHassanId, Doctor: DrKarimId, Rating: 4, Comment: "Very professional and experienced. Explained my MRI options clearly. Clinic was busy but worth the wait."),
            (Patient: PatRanaId, Doctor: DrLaylaId, Rating: 5, Comment: "Finally a doctor who took my migraines seriously. Dr. Layla's treatment plan has reduced my episodes significantly."),
            (Patient: PatYoussefId, Doctor: DrTarekId, Rating: 4, Comment: "Dr. Tarek listened carefully and explained what triggers my reflux. The diet changes are already helping."),
            (Patient: PatMohamedId, Doctor: DrDinaId, Rating: 5, Comment: "Dr. Dina was wonderful with our son: patient, gentle and quick. The vaccination went smoothly and she answered all our questions."),
        };

        var reviews = new List<Review>();

        foreach (var (patient, doctor, rating, comment) in reviewData)
        {
            var appointment = appointments.FirstOrDefault(a =>
                a.Patient.UserId == patient && a.Doctor.UserId == doctor && a.Status == AppointmentStatus.Completed);
            if (appointment is null) continue;

            var reviewedAt = SlotEndUtc(appointment, clock).AddHours(6);
            reviews.Add(new Review
            {
                AppointmentId = appointment.Id,
                PatientProfileId = appointment.PatientProfileId,
                DoctorProfileId = appointment.DoctorProfileId,
                Rating = rating,
                Comment = comment,
                CreatedAt = reviewedAt < now ? reviewedAt : now,
                IsVisible = true,
            });
        }

        if (reviews.Count == 0) return;

        await context.Reviews.AddRangeAsync(reviews);

        foreach (var group in reviews.GroupBy(r => r.DoctorProfileId))
        {
            var doctor = await context.DoctorProfiles.FirstAsync(d => d.Id == group.Key);
            doctor.TotalReviews = group.Count();
            doctor.AverageRating = group.Average(r => r.Rating);
        }

        await context.SaveChangesAsync();
    }

    // ── 8. Notifications ──────────────────────────────────────────────────────
    // Mirrors what the app sends, with the same titles and wording as AppointmentService: the patient
    // and the doctor hear about every booking and cancellation, the patient about completions. Anything
    // older than two days is marked read.
    private static async Task SeedNotificationsAsync(ClinicDbContext context, SeedClock clock)
    {
        if (await context.Notifications.AnyAsync()) return;

        var appointments = await LoadAppointmentsAsync(context);
        if (appointments.Count == 0) return;

        var notifications = new List<Notification>();
        foreach (var appointment in appointments)
            AddAppointmentNotifications(notifications, appointment, clock);

        await context.Notifications.AddRangeAsync(notifications);
        await context.SaveChangesAsync();
    }

    // ── Demo data helpers ─────────────────────────────────────────────────────

    // The notifications the app would have sent for this appointment so far. Every upcoming confirmed visit
    // gets ReminderSentAt, so AppointmentReminderJob never emails demo addresses (some look like real Gmail
    // accounts); visits starting within 24 hours also get the reminder notification the job would have created.
    private static void AddAppointmentNotifications(List<Notification> notifications, Appointment a, SeedClock clock)
    {
        var now = clock.UtcNow;

        void Notify(Guid userId, string title, string message, NotificationType type, DateTime at)
        {
            var createdAt = at < now ? at : now;
            notifications.Add(new Notification
            {
                UserId = userId,
                Title = title,
                Message = message,
                Type = type,
                IsRead = createdAt < now.AddDays(-2),
                CreatedAt = createdAt,
                RelatedEntityId = a.Id,
            });
        }

        static string Name(ApplicationUser user) => $"{user.FirstName} {user.LastName}";

        var patient = a.Patient.User;
        var doctor = a.Doctor.User;
        var when = $"on {a.Slot.Date} at {a.Slot.StartTime:HH:mm}";

        Notify(patient.Id, "Appointment Confirmed",
            $"Your appointment with Dr. {Name(doctor)} {when} has been booked.",
            NotificationType.AppointmentConfirmed, a.BookedAt);
        Notify(doctor.Id, "New Appointment Booked",
            $"You have a new appointment with {Name(patient)} {when}.",
            NotificationType.SystemAlert, a.BookedAt);

        switch (a.Status)
        {
            case AppointmentStatus.Completed:
                Notify(patient.Id, "Appointment Completed",
                    "Your appointment has been completed. You can now leave a review.",
                    NotificationType.AppointmentCompleted, SlotEndUtc(a, clock));
                break;

            case AppointmentStatus.Cancelled:
                var cancelledAt = a.CancelledAt ?? now;
                Notify(patient.Id, "Appointment Cancelled",
                    $"Your appointment {when} has been cancelled.",
                    NotificationType.AppointmentCancelled, cancelledAt);
                Notify(doctor.Id, "Appointment Cancelled",
                    $"Your appointment with {Name(patient)} {when} has been cancelled.",
                    NotificationType.AppointmentCancelled, cancelledAt);
                break;

            case AppointmentStatus.NoShow:
                Notify(patient.Id, "Missed Appointment",
                    $"Your appointment {when} was marked as missed.",
                    NotificationType.SystemAlert, SlotEndUtc(a, clock).AddMinutes(15));
                break;

            case AppointmentStatus.Confirmed when SlotStart(a) > clock.ClinicNow:
                a.ReminderSentAt = now;
                if (SlotStart(a) <= clock.ClinicNow.AddHours(24))
                    Notify(patient.Id, "Appointment Reminder",
                        $"Reminder: You have an appointment {when}.",
                        NotificationType.AppointmentReminder, now.AddHours(-1));
                break;
        }
    }

    // The date `offset` working days from today: -1 is the last working day before today, 1 the next one after it.
    private static DateOnly WorkDay(DateOnly today, int offset)
    {
        var date = today;
        for (var remaining = Math.Abs(offset); remaining > 0;)
        {
            date = date.AddDays(Math.Sign(offset));
            if (WorkDays.Contains(date.DayOfWeek)) remaining--;
        }
        return date;
    }

    // Clinic-local slot start, for comparing with clock.ClinicNow
    private static DateTime SlotStart(Appointment appointment) =>
        appointment.Slot.Date.ToDateTime(appointment.Slot.StartTime);

    // UTC slot end, for timestamps
    // Demo history goes through the same transitions as the app (docs/v2/adr/0004), so it can't hold a state the
    // rules forbid, and it gets the timestamps they record. A rule it breaks is a seeder bug, so it stops the seed
    private static Appointment Book(AppointmentSlot slot, int patientProfileId, string? reason, DateTime bookedAt)
    {
        var appointment = Appointment.Book(slot, patientProfileId, reason, bookedAt);
        appointment.Slot = slot; // the transitions' time guards read it
        return appointment;
    }

    // What happened to a booked visit that wasn't cancelled: completed (with the doctor's notes) once it ended,
    // or missed and marked after the grace period. Confirmed stays as booked
    private static void ApplyHistory(Appointment appointment, AppointmentStatus status, string? notes, SeedClock clock)
    {
        var endUtc = SlotEndUtc(appointment, clock);
        switch (status)
        {
            case AppointmentStatus.Completed:
                Apply(appointment, appointment.Complete(endUtc < clock.UtcNow ? endUtc : clock.UtcNow));
                if (notes is not null)
                    Apply(appointment, appointment.AddDoctorNotes(notes));
                break;
            case AppointmentStatus.NoShow:
                var markedAt = endUtc + Appointment.NoShowGracePeriod;
                Apply(appointment, appointment.MarkNoShow(markedAt < clock.UtcNow ? markedAt : clock.UtcNow));
                break;
            case AppointmentStatus.Confirmed:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(status), status, "The demo seed doesn't create this status");
        }
    }

    private static void Apply(Appointment appointment, Result transition)
    {
        if (transition.IsFailure)
            throw new InvalidOperationException(
                $"Demo appointment for slot {appointment.SlotId} breaks a rule: {transition.Errors[0].Code}");
    }

    private static DateTime SlotEndUtc(Appointment appointment, SeedClock clock) =>
        clock.ToUtc(appointment.Slot.Date.ToDateTime(appointment.Slot.EndTime));

    private static Task<List<Appointment>> LoadAppointmentsAsync(ClinicDbContext context) =>
        context.Appointments
            .Include(a => a.Slot)
            .Include(a => a.Patient).ThenInclude(p => p.User)
            .Include(a => a.Doctor).ThenInclude(d => d.User)
            .ToListAsync();

    // ── Bootstrap admin (all environments) ────────────────────────────────────
    private static async Task SeedBootstrapAdminAsync(
        UserManager<ApplicationUser> userManager, string email, string password, DateTime utcNow)
    {
        if (await userManager.FindByEmailAsync(email) is not null) return;

        var admin = new ApplicationUser
        {
            FirstName = "Clinic",
            LastName = "Admin",
            Email = email,
            UserName = email,
            CreatedAt = utcNow,
            IsActive = true,
            EmailConfirmed = true,
        };

        var result = await userManager.CreateAsync(admin, password);
        if (!result.Succeeded)
            throw new InvalidOperationException(
                "Could not create the bootstrap admin from Seeding:AdminEmail/AdminPassword: "
                + string.Join(", ", result.Errors.Select(e => e.Description)));

        await userManager.AddToRoleAsync(admin, Roles.Admin);
    }

    // ── Helper ────────────────────────────────────────────────────────────────
    private static async Task CreateUserAsync(
        UserManager<ApplicationUser> userManager,
        ApplicationUser user,
        string password,
        string role)
    {
        if (await userManager.FindByEmailAsync(user.Email!) is not null) return;

        var result = await userManager.CreateAsync(user, password);
        if (result.Succeeded)
            await userManager.AddToRoleAsync(user, role);
    }
}