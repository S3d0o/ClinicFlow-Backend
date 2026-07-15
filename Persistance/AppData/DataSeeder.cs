using ClinicFlow.Domain.Enums;
using Domain.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Shared.Enums;

namespace Persistence.AppData;

/// <summary>
/// Seeds the database with realistic Egyptian clinic data.
/// Call DataSeeder.SeedAsync(app.Services) in Program.cs before app.Run().
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
/// </summary>
public static class DataSeeder
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

    // ── Entry point ───────────────────────────────────────────────────────────
    public static async Task SeedAsync(IServiceProvider service)
    {
        using var scope = service.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<ClinicDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();

        // Applies any pending migrations automatically — safe to call on every startup.
        await context.Database.MigrateAsync();

        await SeedRolesAsync(roleManager);
        await SeedSpecialtiesAsync(context);
        await SeedUsersAsync(userManager, context);
        await SeedDoctorSchedulesAsync(context);
        await SeedAppointmentSlotsAsync(context);
        await SeedAppointmentsAsync(context);
        await SeedReviewsAsync(context);
        await SeedNotificationsAsync(context);
    }

    // ── 1. Roles ──────────────────────────────────────────────────────────────
    private static async Task SeedRolesAsync(RoleManager<IdentityRole<Guid>> roleManager)
    {
        foreach (var role in new[] { "Admin", "Doctor", "Patient" })
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
    private static async Task SeedUsersAsync(UserManager<ApplicationUser> userManager, ClinicDbContext context)
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
            CreatedAt = DateTime.UtcNow,
            IsActive = true,
            EmailConfirmed = true,
        }, "Admin@1234", "Admin");

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
                CreatedAt      = DateTime.UtcNow,
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
                AverageRating     = 4.8,
                TotalReviews      = 4,
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
                CreatedAt      = DateTime.UtcNow,
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
                AverageRating     = 4.6,
                TotalReviews      = 3,
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
                CreatedAt      = DateTime.UtcNow,
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
                AverageRating     = 4.7,
                TotalReviews      = 3,
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
                CreatedAt      = DateTime.UtcNow,
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
                AverageRating     = 4.9,
                TotalReviews      = 3,
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
                CreatedAt      = DateTime.UtcNow,
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
                AverageRating     = 4.5,
                TotalReviews      = 2,
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
                CreatedAt      = DateTime.UtcNow,
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
                AverageRating     = 4.7,
                TotalReviews      = 2,
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
                CreatedAt      = DateTime.UtcNow,
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
                AverageRating     = 0.0,
                TotalReviews      = 0,
                IsApprovedByAdmin = false,
                ClinicAddress     = "10 Abbas El-Akkad Street, Nasr City",
                ClinicCity        = "Cairo",
            }),
        };

        foreach (var (user, profile) in doctors)
        {
            await CreateUserAsync(userManager, user, "Doctor@1234", "Doctor");
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
                CreatedAt      = DateTime.UtcNow,
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
                CreatedAt      = DateTime.UtcNow,
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
                CreatedAt      = DateTime.UtcNow,
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
                CreatedAt      = DateTime.UtcNow,
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
                CreatedAt      = DateTime.UtcNow,
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
            await CreateUserAsync(userManager, user, "Patient@1234", "Patient");
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
            var workDays = new[] { DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday };

            foreach (var day in workDays)
            {
                schedules.Add(new DoctorSchedule
                {
                    DoctorProfileId = doctor.Id,
                    DayOfWeek = day,
                    StartTime = new TimeOnly(9, 0),
                    EndTime = new TimeOnly(13, 0),
                    SlotDurationMinutes = 30,
                    IsActive = true,
                });

                schedules.Add(new DoctorSchedule
                {
                    DoctorProfileId = doctor.Id,
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
    private static async Task SeedAppointmentSlotsAsync(ClinicDbContext context)
    {
        if (await context.AppointmentSlots.AnyAsync()) return;

        var schedules = await context.DoctorSchedules
            .Where(s => s.IsActive)
            .ToListAsync();

        var slots = new List<AppointmentSlot>();

        // Generate slots for: 14 past days (history) + today + 21 future days (booking window).
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        for (int dayOffset = -14; dayOffset <= 21; dayOffset++)
        {
            var date = today.AddDays(dayOffset);
            var dayOfWk = date.DayOfWeek;

            foreach (var schedule in schedules.Where(s => s.DayOfWeek == dayOfWk))
            {
                var current = schedule.StartTime;
                while (current.AddMinutes(schedule.SlotDurationMinutes) <= schedule.EndTime)
                {
                    slots.Add(new AppointmentSlot
                    {
                        DoctorProfileId = schedule.DoctorProfileId,
                        DoctorScheduleId = schedule.Id,
                        Date = date,
                        StartTime = current,
                        EndTime = current.AddMinutes(schedule.SlotDurationMinutes),
                        Status = SlotStatus.Available,
                    });
                    current = current.AddMinutes(schedule.SlotDurationMinutes);
                }
            }
        }

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
    // Dates are pinned to July 2026 so the seeder looks correct regardless of when it runs.
    private static async Task SeedAppointmentsAsync(ClinicDbContext context)
    {
        if (await context.Appointments.AnyAsync()) return;

        var patients = await context.PatientProfiles.ToListAsync();
        var slots = await context.AppointmentSlots
            .OrderBy(s => s.DoctorProfileId)
            .ThenBy(s => s.Date)
            .ThenBy(s => s.StartTime)
            .ToListAsync();

        if (!slots.Any() || !patients.Any()) return;

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        AppointmentSlot? Slot(Guid doctorUserId, DateOnly date, TimeOnly time)
        {
            var doctorProfileId = context.DoctorProfiles
                .Where(d => d.UserId == doctorUserId)
                .Select(d => d.Id)
                .FirstOrDefault();

            if (doctorProfileId == 0) return null;

            return slots.FirstOrDefault(s =>
                s.DoctorProfileId == doctorProfileId &&
                s.Date == date &&
                s.StartTime == time &&
                s.Status == SlotStatus.Available);
        }

        PatientProfile Patient(Guid userId) =>
            patients.First(p => p.UserId == userId);

        var appointments = new List<Appointment>();

        // ── Past: Completed appointments ─────────────────────────────────────
        // Note: past slots don't exist in DB, so we create them inline

        var s0 = Slot(DrAhmedId, today.AddDays(-1), new TimeOnly(9, 0));
        if (s0 != null) { appointments.Add(new Appointment { SlotId = s0.Id, PatientProfileId = Patient(PatMohamedId).Id, DoctorProfileId = s0.DoctorProfileId, Status = AppointmentStatus.Completed, ReasonForVisit = "Chest pain and shortness of breath during exercise.", DoctorNotes = "Mild hypertension detected. ECG normal. Prescribed Amlodipine 5 mg once daily. Advised low-sodium diet. Follow-up in 4 weeks.", BookedAt = DateTime.UtcNow.AddDays(-3) }); s0.Status = SlotStatus.Booked; }

        var s1 = Slot(DrNourId, today.AddDays(-2), new TimeOnly(9, 30));
        if (s1 != null) { appointments.Add(new Appointment { SlotId = s1.Id, PatientProfileId = Patient(PatSaraId).Id, DoctorProfileId = s1.DoctorProfileId, Status = AppointmentStatus.Completed, ReasonForVisit = "Persistent skin rash on forearms and neck for three weeks.", DoctorNotes = "Contact dermatitis, likely triggered by new detergent. Prescribed Hydrocortisone 1% cream twice daily for 7 days. Avoid irritants.", BookedAt = DateTime.UtcNow.AddDays(-5) }); s1.Status = SlotStatus.Booked; }

        var s2 = Slot(DrKarimId, today.AddDays(-3), new TimeOnly(10, 0));
        if (s2 != null) { appointments.Add(new Appointment { SlotId = s2.Id, PatientProfileId = Patient(PatHassanId).Id, DoctorProfileId = s2.DoctorProfileId, Status = AppointmentStatus.Completed, ReasonForVisit = "Right knee pain and swelling after football match.", DoctorNotes = "MRI ordered — suspected partial ACL tear. Advised RICE protocol and physiotherapy 3x/week. NSAIDs for pain management. Follow-up in 2 weeks.", BookedAt = DateTime.UtcNow.AddDays(-4) }); s2.Status = SlotStatus.Booked; }

        var s3 = Slot(DrLaylaId, today.AddDays(-4), new TimeOnly(9, 0));
        if (s3 != null) { appointments.Add(new Appointment { SlotId = s3.Id, PatientProfileId = Patient(PatRanaId).Id, DoctorProfileId = s3.DoctorProfileId, Status = AppointmentStatus.Completed, ReasonForVisit = "Severe recurring migraines, 3–4 episodes per week.", DoctorNotes = "Chronic migraine confirmed. Started on Topiramate 25 mg nightly as prophylaxis. Sumatriptan 50 mg for acute attacks. Keep headache diary.", BookedAt = DateTime.UtcNow.AddDays(-7) }); s3.Status = SlotStatus.Booked; }

        var s4 = Slot(DrTarekId, today.AddDays(-3), new TimeOnly(10, 0));
        if (s4 != null) { appointments.Add(new Appointment { SlotId = s4.Id, PatientProfileId = Patient(PatYoussefId).Id, DoctorProfileId = s4.DoctorProfileId, Status = AppointmentStatus.Completed, ReasonForVisit = "Recurring acid reflux and upper abdominal discomfort after meals.", BookedAt = DateTime.UtcNow.AddDays(-6) }); s4.Status = SlotStatus.Booked; }

        var s5 = Slot(DrDinaId, today.AddDays(-2), new TimeOnly(9, 0));
        if (s5 != null) { appointments.Add(new Appointment { SlotId = s5.Id, PatientProfileId = Patient(PatMohamedId).Id, DoctorProfileId = s5.DoctorProfileId, Status = AppointmentStatus.Completed, ReasonForVisit = "Child's 18-month routine checkup and vaccination.", BookedAt = DateTime.UtcNow.AddDays(-5) }); s5.Status = SlotStatus.Booked; }

        var s6 = Slot(DrAhmedId, today.AddDays(-2), new TimeOnly(9, 30));
        if (s6 != null) { appointments.Add(new Appointment { SlotId = s6.Id, PatientProfileId = Patient(PatSaraId).Id, DoctorProfileId = s6.DoctorProfileId, Status = AppointmentStatus.Completed, ReasonForVisit = "Palpitations and irregular heartbeat noticed for two days.", BookedAt = DateTime.UtcNow.AddDays(-4) }); s6.Status = SlotStatus.Booked; }

        // ── Cancelled ────────────────────────────────────────────────────────
        var s7 = Slot(DrNourId, today.AddDays(5), new TimeOnly(10, 0));
        if (s7 != null) { appointments.Add(new Appointment { SlotId = s7.Id, PatientProfileId = Patient(PatRanaId).Id, DoctorProfileId = s7.DoctorProfileId, Status = AppointmentStatus.Cancelled, ReasonForVisit = "Acne treatment consultation.", BookedAt = DateTime.UtcNow.AddDays(-3), CancelledAt = DateTime.UtcNow.AddDays(-1), CancellationReason = "Personal schedule conflict — will rebook next week.", CancelledBy = CancelledBy.Patient }); s7.Status = SlotStatus.Booked; }

        var s8 = Slot(DrTarekId, today.AddDays(6), new TimeOnly(16, 0));
        if (s8 != null) { appointments.Add(new Appointment { SlotId = s8.Id, PatientProfileId = Patient(PatHassanId).Id, DoctorProfileId = s8.DoctorProfileId, Status = AppointmentStatus.Cancelled, ReasonForVisit = "Follow-up after colonoscopy results.", BookedAt = DateTime.UtcNow.AddDays(-4), CancelledAt = DateTime.UtcNow.AddDays(-1), CancellationReason = "Doctor unavailable due to emergency surgery. Slot rescheduled.", CancelledBy = CancelledBy.Doctor }); s8.Status = SlotStatus.Booked; }

        // ── NoShow ───────────────────────────────────────────────────────────
        var s9 = Slot(DrKarimId, today.AddDays(5), new TimeOnly(9, 0));
        if (s9 != null) { appointments.Add(new Appointment { SlotId = s9.Id, PatientProfileId = Patient(PatYoussefId).Id, DoctorProfileId = s9.DoctorProfileId, Status = AppointmentStatus.NoShow, ReasonForVisit = "Right shoulder impingement follow-up.", BookedAt = DateTime.UtcNow.AddDays(-3) }); s9.Status = SlotStatus.Booked; }

        // ── Upcoming: Confirmed ───────────────────────────────────────────────
        var s10 = Slot(DrAhmedId, today.AddDays(1), new TimeOnly(9, 0));
        if (s10 != null) { appointments.Add(new Appointment { SlotId = s10.Id, PatientProfileId = Patient(PatMohamedId).Id, DoctorProfileId = s10.DoctorProfileId, Status = AppointmentStatus.Confirmed, ReasonForVisit = "Follow-up on Amlodipine — blood pressure monitoring.", BookedAt = DateTime.UtcNow.AddDays(-2) }); s10.Status = SlotStatus.Booked; }

        var s11 = Slot(DrNourId, today.AddDays(2), new TimeOnly(10, 0));
        if (s11 != null) { appointments.Add(new Appointment { SlotId = s11.Id, PatientProfileId = Patient(PatSaraId).Id, DoctorProfileId = s11.DoctorProfileId, Status = AppointmentStatus.Confirmed, ReasonForVisit = "Laser session for pigmentation on cheeks.", BookedAt = DateTime.UtcNow.AddDays(-1) }); s11.Status = SlotStatus.Booked; }

        var s12 = Slot(DrKarimId, today.AddDays(3), new TimeOnly(16, 0));
        if (s12 != null) { appointments.Add(new Appointment { SlotId = s12.Id, PatientProfileId = Patient(PatHassanId).Id, DoctorProfileId = s12.DoctorProfileId, Status = AppointmentStatus.Confirmed, ReasonForVisit = "MRI results review and ACL treatment plan.", BookedAt = DateTime.UtcNow.AddDays(-1) }); s12.Status = SlotStatus.Booked; }

        var s13 = Slot(DrLaylaId, today.AddDays(6), new TimeOnly(9, 0));
        if (s13 != null) { appointments.Add(new Appointment { SlotId = s13.Id, PatientProfileId = Patient(PatRanaId).Id, DoctorProfileId = s13.DoctorProfileId, Status = AppointmentStatus.Confirmed, ReasonForVisit = "Migraine follow-up — Topiramate dosage check.", BookedAt = DateTime.UtcNow.AddDays(-1) }); s13.Status = SlotStatus.Booked; }

        var s14 = Slot(DrTarekId, today.AddDays(7), new TimeOnly(16, 30));
        if (s14 != null) { appointments.Add(new Appointment { SlotId = s14.Id, PatientProfileId = Patient(PatYoussefId).Id, DoctorProfileId = s14.DoctorProfileId, Status = AppointmentStatus.Confirmed, ReasonForVisit = "GERD follow-up — 4-week Omeprazole check.", BookedAt = DateTime.UtcNow.AddDays(-1) }); s14.Status = SlotStatus.Booked; }

        var s15 = Slot(DrDinaId, today.AddDays(9), new TimeOnly(9, 30));
        if (s15 != null) { appointments.Add(new Appointment { SlotId = s15.Id, PatientProfileId = Patient(PatMohamedId).Id, DoctorProfileId = s15.DoctorProfileId, Status = AppointmentStatus.Confirmed, ReasonForVisit = "Child fever and ear pain for two days.", BookedAt = DateTime.UtcNow.AddDays(-1) }); s15.Status = SlotStatus.Booked; }

        var s16 = Slot(DrLaylaId, today.AddDays(13), new TimeOnly(10, 0));
        if (s16 != null) { appointments.Add(new Appointment { SlotId = s16.Id, PatientProfileId = Patient(PatSaraId).Id, DoctorProfileId = s16.DoctorProfileId, Status = AppointmentStatus.Confirmed, ReasonForVisit = "Frequent numbness in left arm — concerned about nerve issues.", BookedAt = DateTime.UtcNow.AddDays(-1) }); s16.Status = SlotStatus.Booked; }

        var s17 = Slot(DrAhmedId, today.AddDays(-1), new TimeOnly(16, 0));
        if (s17 != null) { appointments.Add(new Appointment { SlotId = s17.Id, PatientProfileId = Patient(PatHassanId).Id, DoctorProfileId = s17.DoctorProfileId, Status = AppointmentStatus.Confirmed, ReasonForVisit = "Routine cardiac checkup and blood pressure review.", BookedAt = DateTime.UtcNow.AddDays(-2) }); s17.Status = SlotStatus.Booked; }
        
        await context.Appointments.AddRangeAsync(appointments);
        await context.SaveChangesAsync();
    }
    // ── 7. Reviews ────────────────────────────────────────────────────────────
    // Only completed appointments [0]–[6] are eligible.
    private static async Task SeedReviewsAsync(ClinicDbContext context)
    {
        if (await context.Reviews.AnyAsync()) return;

        var completed = await context.Appointments
            .Where(a => a.Status == AppointmentStatus.Completed)
            .OrderBy(a => a.BookedAt)
            .ToListAsync();

        if (!completed.Any()) return;

        var reviewData = new[]
        {
        (Rating: 5, Comment: "Dr. Ahmed is exceptional. He explained my condition in detail and the treatment is already making a difference. Highly recommend."),
        (Rating: 5, Comment: "Dr. Nour was very thorough and kind. The cream she prescribed cleared the rash completely within a week."),
        (Rating: 4, Comment: "Very professional and experienced. Explained my MRI options clearly. Clinic was busy but worth the wait."),
        (Rating: 5, Comment: "Finally a doctor who took my migraines seriously. Dr. Layla's treatment plan has reduced my episodes significantly."),
        };

        var reviews = new List<Review>();

        for (int i = 0; i < Math.Min(completed.Count, reviewData.Length); i++)
        {
            reviews.Add(new Review
            {
                AppointmentId = completed[i].Id,
                PatientProfileId = completed[i].PatientProfileId,
                DoctorProfileId = completed[i].DoctorProfileId,
                Rating = reviewData[i].Rating,
                Comment = reviewData[i].Comment,
                CreatedAt = DateTime.UtcNow.AddDays(-i),
                IsVisible = true,
            });
        }

        await context.Reviews.AddRangeAsync(reviews);
        await context.SaveChangesAsync();
    }
    // ── 8. Notifications ──────────────────────────────────────────────────────
    private static async Task SeedNotificationsAsync(ClinicDbContext context)
    {
        if (await context.Notifications.AnyAsync()) return;

        var appointments = await context.Appointments
            .OrderBy(a => a.BookedAt)
            .ToListAsync();

        if (!appointments.Any()) return;

        var notifications = new List<Notification>();

        void AddNotification(Guid userId, string title, string message, NotificationType type, bool isRead, int appointmentIndex)
        {
            if (appointmentIndex >= appointments.Count) return;
            notifications.Add(new Notification
            {
                UserId = userId,
                Title = title,
                Message = message,
                Type = type,
                IsRead = isRead,
                CreatedAt = DateTime.UtcNow.AddDays(-1),
                RelatedEntityId = appointments[appointmentIndex].Id,
            });
        }

        // ── Booking confirmations (historical) ────────────────────────────
        AddNotification(PatMohamedId, "Appointment Confirmed",
            "Your appointment with Dr. Ahmed El-Sayed has been booked.",
            NotificationType.AppointmentConfirmed, true, 0);

        AddNotification(PatSaraId, "Appointment Confirmed",
            "Your appointment with Dr. Nour Hassan has been booked.",
            NotificationType.AppointmentConfirmed, true, 1);

        // ── Completion notifications ──────────────────────────────────────
        AddNotification(PatMohamedId, "Appointment Completed",
            "Your appointment with Dr. Ahmed El-Sayed is complete. You can now leave a review.",
            NotificationType.AppointmentCompleted, true, 0);

        AddNotification(PatSaraId, "Appointment Completed",
            "Your appointment with Dr. Nour Hassan is complete. You can now leave a review.",
            NotificationType.AppointmentCompleted, true, 1);

        AddNotification(PatRanaId, "Appointment Completed",
            "Your appointment with Dr. Layla Naguib is complete. You can now leave a review.",
            NotificationType.AppointmentCompleted, false, 3);

        // ── Cancellation notifications ────────────────────────────────────
        AddNotification(PatRanaId, "Appointment Cancelled",
            "Your appointment has been cancelled successfully.",
            NotificationType.AppointmentCancelled, true, 7);

        AddNotification(PatHassanId, "Appointment Cancelled",
            "Your appointment has been cancelled by the doctor. We apologize for the inconvenience.",
            NotificationType.AppointmentCancelled, false, 8);

        // ── Reminder ──────────────────────────────────────────────────────
        AddNotification(PatMohamedId, "Appointment Reminder",
            "Reminder: You have an upcoming appointment with Dr. Ahmed El-Sayed. Please arrive 10 minutes early.",
            NotificationType.AppointmentReminder, false, 10);

        // ── Upcoming booking confirmations ────────────────────────────────
        AddNotification(PatSaraId, "Appointment Confirmed",
            "Your appointment with Dr. Nour Hassan has been booked.",
            NotificationType.AppointmentConfirmed, false, 11);

        AddNotification(PatHassanId, "Appointment Confirmed",
            "Your appointment with Dr. Karim Mansour has been booked.",
            NotificationType.AppointmentConfirmed, false, 12);

        AddNotification(PatRanaId, "Appointment Confirmed",
            "Your appointment with Dr. Layla Naguib has been booked.",
            NotificationType.AppointmentConfirmed, false, 13);

        AddNotification(PatYoussefId, "Appointment Confirmed",
            "Your appointment with Dr. Tarek Soliman has been booked.",
            NotificationType.AppointmentConfirmed, false, 14);

        // ── System alert ──────────────────────────────────────────────────
        AddNotification(PatYoussefId, "No-Show Recorded",
            "You missed your appointment with Dr. Karim Mansour. Please book a new appointment if needed.",
            NotificationType.SystemAlert, false, 9);

        await context.Notifications.AddRangeAsync(notifications);
        await context.SaveChangesAsync();
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