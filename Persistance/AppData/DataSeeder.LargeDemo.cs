using ClinicFlow.Domain.Enums;
using Domain.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Persistence.Transactions;
using Shared.Enums;

namespace Persistence.AppData;

// The generated part of the demo clinic, on top of the hand-written accounts in DataSeeder.cs:
// 53 doctors and 75 patients, about 2,500 appointments over SlotHistoryDays of history and
// SlotBookingWindowDays ahead, reviews and notifications. A fixed random seed makes every fresh
// database identical, so a problem seen once can be reproduced. Development only (Seeding:DemoData).
public static partial class DataSeeder
{
    private const int GeneratedDoctorCount = 53;     // 60 doctors with the 7 hand-written ones
    private const int GeneratedPatientCount = 75;    // 80 patients with the 5 hand-written ones
    private const int PendingGeneratedDoctors = 5;   // the last 5: waiting for approval, so no schedule
    private const int SuspendedGeneratedDoctors = 3; // the 3 before them: approved, later suspended
    private const int TargetPastAppointments = 1900;
    private const int TargetUpcomingAppointments = 600;

    // .test is a reserved top-level domain (RFC 6761): mail to it is never delivered
    private const string GeneratedEmailDomain = "clinicflow.test";

    private static Guid GeneratedDoctorId(int n) => Guid.Parse($"00000000-0000-0000-0001-{n:D12}");
    private static Guid GeneratedPatientId(int n) => Guid.Parse($"00000000-0000-0000-0002-{n:D12}");

    // ── Generated people: users, profiles, schedules ──────────────────────────
    // Users are inserted directly with one precomputed hash per password: going through UserManager
    // would hash 128 passwords one by one and add seconds to the first start.
    private static async Task SeedGeneratedPeopleAsync(ClinicDbContext context, SeedClock clock, ILogger logger)
    {
        var firstGeneratedDoctor = GeneratedDoctorId(1);
        if (await context.Users.AnyAsync(u => u.Id == firstGeneratedDoctor)) return;

        var rng = new Random(20261018);
        var specialties = await context.Specialties.Where(s => s.IsActive).OrderBy(s => s.Id).ToListAsync();
        var roles = await context.Roles.ToDictionaryAsync(r => r.Name!, r => r.Id);
        var hasher = new PasswordHasher<ApplicationUser>();
        var doctorHash = hasher.HashPassword(new ApplicationUser(), "Doctor@1234");
        var patientHash = hasher.HashPassword(new ApplicationUser(), "Patient@1234");

        (Gender Gender, string First, string Last) NewName()
        {
            var gender = rng.Next(2) == 0 ? Gender.Male : Gender.Female;
            var firstNames = gender == Gender.Male ? MaleFirstNames : FemaleFirstNames;
            return (gender, Pick(rng, firstNames), Pick(rng, LastNames));
        }

        ApplicationUser NewUser(Guid id, string email, (Gender Gender, string First, string Last) name,
            DateOnly dateOfBirth, string passwordHash, bool isActive, DateTime createdAt) => new()
        {
            Id = id,
            FirstName = name.First,
            LastName = name.Last,
            Email = email,
            UserName = email,
            NormalizedEmail = email.ToUpperInvariant(),
            NormalizedUserName = email.ToUpperInvariant(),
            EmailConfirmed = true,
            PasswordHash = passwordHash,
            SecurityStamp = Guid.NewGuid().ToString("N").ToUpperInvariant(),
            ConcurrencyStamp = Guid.NewGuid().ToString(),
            LockoutEnabled = true,
            PhoneNumber = $"+2010{rng.Next(10_000_000, 100_000_000)}",
            Gender = name.Gender,
            DateOfBirth = dateOfBirth,
            CreatedAt = createdAt,
            IsActive = isActive,
        };

        var users = new List<ApplicationUser>();
        var userRoles = new List<IdentityUserRole<Guid>>();
        var doctors = new List<(int N, DoctorProfile Profile)>();

        for (var n = 1; n <= GeneratedDoctorCount; n++)
        {
            var pending = n > GeneratedDoctorCount - PendingGeneratedDoctors;
            var suspended = !pending && n > GeneratedDoctorCount - PendingGeneratedDoctors - SuspendedGeneratedDoctors;
            var specialty = specialties[(n - 1) % specialties.Count];
            var experience = rng.Next(2, 36);
            var city = Pick(rng, Cities);
            var id = GeneratedDoctorId(n);

            users.Add(NewUser(id, $"doctor{n:D2}@{GeneratedEmailDomain}", NewName(),
                new DateOnly(clock.Today.Year - experience - rng.Next(25, 31), rng.Next(1, 13), rng.Next(1, 29)),
                doctorHash, isActive: !suspended,
                createdAt: clock.UtcNow.AddDays(pending ? -rng.Next(1, 10) : -rng.Next(120, 720))));
            userRoles.Add(new IdentityUserRole<Guid> { UserId = id, RoleId = roles[Roles.Doctor] });
            doctors.Add((n, new DoctorProfile
            {
                UserId = id,
                SpecialtyId = specialty.Id,
                Bio = $"{SpecialtyFocus.GetValueOrDefault(specialty.Name, $"{specialty.Name} consultant.")} " +
                      $"{experience} years of experience, practising in {city.Name}.",
                YearsOfExperience = experience,
                ConsultationFee = 200 + 50 * rng.Next(0, 15),
                IsApprovedByAdmin = !pending,
                ClinicAddress = $"{rng.Next(1, 150)} {Pick(rng, Streets)}, {Pick(rng, city.Districts)}",
                ClinicCity = city.Name,
            }));
        }

        var patients = new List<PatientProfile>();
        for (var n = 1; n <= GeneratedPatientCount; n++)
        {
            var name = NewName();
            var id = GeneratedPatientId(n);
            users.Add(NewUser(id, $"patient{n:D2}@{GeneratedEmailDomain}", name,
                new DateOnly(rng.Next(1950, 2007), rng.Next(1, 13), rng.Next(1, 29)),
                patientHash, isActive: true, createdAt: clock.UtcNow.AddDays(-rng.Next(90, 720))));
            userRoles.Add(new IdentityUserRole<Guid> { UserId = id, RoleId = roles[Roles.Patient] });

            var hasEmergencyContact = rng.NextDouble() < 0.6;
            var contact = NewName();
            patients.Add(new PatientProfile
            {
                UserId = id,
                BloodType = (BloodType)rng.Next(1, 9),
                Allergies = rng.NextDouble() < 0.3 ? Pick(rng, Allergies) : null,
                ChronicConditions = rng.NextDouble() < 0.35 ? Pick(rng, ChronicConditions) : null,
                EmergencyContactName = hasEmergencyContact ? $"{contact.First} {name.Last}" : null,
                EmergencyContactPhone = hasEmergencyContact ? $"+2011{rng.Next(10_000_000, 100_000_000)}" : null,
            });
        }

        // One transaction: the check at the top must never find half a clinic
        // Inside the SQL retry strategy (REVIEW.md NL14); a transient error re-runs the block from a rewound tracker
        var scheduleCount = await context.ExecuteInTransactionAsync(async transaction =>
        {
            context.Users.AddRange(users);
            context.UserRoles.AddRange(userRoles);
            context.DoctorProfiles.AddRange(doctors.Select(d => d.Profile));
            context.PatientProfiles.AddRange(patients);
            await context.SaveChangesAsync();

            // Approved doctors (suspended ones included) get one schedule per working weekday, as the app allows
            var schedules = new List<DoctorSchedule>();
            foreach (var (n, profile) in doctors.Where(d => d.Profile.IsApprovedByAdmin))
            {
                foreach (var shift in ScheduleTemplates[n % ScheduleTemplates.Length])
                {
                    schedules.Add(new DoctorSchedule
                    {
                        DoctorProfileId = profile.Id,
                        BranchId = Branch.MainId,
                        DayOfWeek = shift.Day,
                        StartTime = shift.Start,
                        EndTime = shift.End,
                        SlotDurationMinutes = shift.SlotMinutes,
                        IsActive = true,
                    });
                }
            }
            context.DoctorSchedules.AddRange(schedules);
            await context.SaveChangesAsync();
            await transaction.CommitAsync(CancellationToken.None);
            return schedules.Count;
        }, CancellationToken.None);

        logger.LogInformation("Generated demo clinic: {Doctors} doctors, {Patients} patients, {Schedules} schedules",
            doctors.Count, patients.Count, scheduleCount);
    }

    // ── Generated activity: appointments, reviews, notifications ──────────────
    // Runs once the slots exist. Each doctor gets a popularity (how full their calendar gets) and a
    // quality (their average rating); both are random but fixed by the seed.
    private static async Task SeedGeneratedActivityAsync(ClinicDbContext context, SeedClock clock, ILogger logger)
    {
        var generatedPatientUserIds = Enumerable.Range(1, GeneratedPatientCount).Select(GeneratedPatientId).ToList();
        var patientIds = await context.PatientProfiles
            .Where(p => generatedPatientUserIds.Contains(p.UserId))
            .OrderBy(p => p.Id)
            .Select(p => p.Id)
            .ToListAsync();
        if (patientIds.Count == 0 || await context.Appointments.AnyAsync(a => patientIds.Contains(a.PatientProfileId)))
            return;

        var rng = new Random(20261019);
        // The hand-written doctors are the showcase, so they rate well; better-rated doctors also
        // tend to be busier
        var handWrittenDoctors = new HashSet<Guid> { DrAhmedId, DrNourId, DrKarimId, DrDinaId, DrTarekId, DrLaylaId, DrOmarId };
        var doctors = (await context.DoctorProfiles
                .Where(d => d.IsApprovedByAdmin)
                .OrderBy(d => d.Id)
                .Select(d => new { d.Id, d.UserId, Specialty = d.Specialty.Name, d.User.IsActive })
                .ToListAsync())
            .ToDictionary(d => d.Id, d =>
            {
                var quality = handWrittenDoctors.Contains(d.UserId) ? 4.2 + 0.7 * rng.NextDouble() : 2.8 + 2.1 * rng.NextDouble();
                var popularity = 0.4 + 0.6 * rng.NextDouble() + 0.6 * (quality - 2.8) / 2.1;
                return (d.Specialty, d.IsActive, Popularity: popularity, Quality: quality);
            });
        var doctorIds = doctors.Keys.ToList();

        // Free slots across the whole window. A suspended doctor only has visits from before the suspension
        var firstDate = clock.Today.AddDays(-SlotHistoryDays);
        var suspendedBefore = clock.Today.AddDays(-10);
        var slots = (await context.AppointmentSlots
                .Where(s => s.Status == SlotStatus.Available && s.Date >= firstDate && doctorIds.Contains(s.DoctorProfileId))
                .OrderBy(s => s.Date).ThenBy(s => s.StartTime).ThenBy(s => s.DoctorProfileId)
                .ToListAsync())
            .Where(s => doctors[s.DoctorProfileId].IsActive || s.Date < suspendedBefore)
            .ToList();

        // A slot is booked with probability p × the doctor's popularity, so p = target / Σ popularity
        bool IsPast(AppointmentSlot slot) => slot.Date.ToDateTime(slot.StartTime) < clock.ClinicNow;
        var pastWeight = slots.Where(IsPast).Sum(s => doctors[s.DoctorProfileId].Popularity);
        var upcomingWeight = slots.Where(s => !IsPast(s)).Sum(s => doctors[s.DoctorProfileId].Popularity);
        var pPast = TargetPastAppointments / Math.Max(1.0, pastWeight);
        var pUpcoming = TargetUpcomingAppointments / Math.Max(1.0, upcomingWeight);

        var patientBusy = new HashSet<(int PatientId, DateOnly Date)>();
        var appointments = new List<Appointment>();

        foreach (var slot in slots)
        {
            var doctor = doctors[slot.DoctorProfileId];
            var past = IsPast(slot);
            if (rng.NextDouble() >= (past ? pPast : pUpcoming) * doctor.Popularity) continue;

            // At most one visit per patient per day
            int? patientId = null;
            for (var attempt = 0; attempt < 5 && patientId is null; attempt++)
            {
                var candidate = patientIds[rng.Next(patientIds.Count)];
                if (patientBusy.Add((candidate, slot.Date))) patientId = candidate;
            }
            if (patientId is null) continue;

            var start = slot.Date.ToDateTime(slot.StartTime); // clinic-local
            var status = past ? PastStatus(rng, start, clock) : rng.NextDouble() < 0.9 ? AppointmentStatus.Confirmed : AppointmentStatus.Cancelled;
            var bookedLocal = (past ? start : clock.ClinicNow).AddHours(-rng.Next(2, 14 * 24));

            // The random draws stay in their original order (reason, notes, cancellation), so the data is unchanged
            var reason = Pick(rng, VisitReasons.GetValueOrDefault(doctor.Specialty, GeneralReasons));
            var notes = status == AppointmentStatus.Completed && rng.NextDouble() < 0.75
                ? Pick(rng, DoctorNotesBySpecialty.GetValueOrDefault(doctor.Specialty, GeneralNotes))
                : null;
            var appointment = Book(slot, patientId.Value, reason, clock.ToUtc(bookedLocal));

            if (status == AppointmentStatus.Cancelled)
            {
                var roll = rng.NextDouble();
                var by = roll < 0.6 ? CancelledBy.Patient : roll < 0.9 ? CancelledBy.Doctor : CancelledBy.Admin;
                // Patients can only cancel up to the (default) window before the start; nobody cancels in the future
                var latest = by == CancelledBy.Patient ? start - DefaultSettings.CancellationWindow : start;
                if (latest > clock.ClinicNow) latest = clock.ClinicNow;
                if (latest < bookedLocal) latest = bookedLocal;
                Apply(appointment, appointment.Cancel(by, clock.ToUtc(bookedLocal + (latest - bookedLocal) * rng.NextDouble()),
                    Pick(rng, CancellationReasons[by]), DefaultSettings.CancellationWindow));
                // The slot stays Available, as after a real cancellation
            }
            else
            {
                ApplyHistory(appointment, status, notes, clock);
                slot.Status = SlotStatus.Booked;
            }

            appointments.Add(appointment);
        }

        // Inside the SQL retry strategy (REVIEW.md NL14). A retry repeats the random draws below from where the
        // failed attempt left the generator, so only an uninterrupted seed is reproducible
        var (reviewCount, notificationCount) = await context.ExecuteInTransactionAsync(async transaction =>
        {
            context.Appointments.AddRange(appointments);
            await context.SaveChangesAsync();

            // About 60% of completed visits are reviewed; ratings scatter around the doctor's quality
            var reviews = new List<Review>();
            foreach (var appointment in appointments.Where(a => a.Status == AppointmentStatus.Completed))
            {
                if (rng.NextDouble() >= 0.6) continue;

                var quality = doctors[appointment.DoctorProfileId].Quality;
                var rating = Math.Clamp((int)Math.Round(quality + (rng.NextDouble() + rng.NextDouble() - 1) * 1.8), 1, 5);
                var reviewedAt = SlotEndUtc(appointment, clock).AddHours(rng.Next(1, 72));
                reviews.Add(new Review
                {
                    AppointmentId = appointment.Id,
                    PatientProfileId = appointment.PatientProfileId,
                    DoctorProfileId = appointment.DoctorProfileId,
                    Rating = rating,
                    Comment = rng.NextDouble() < 0.75 ? Pick(rng, ReviewComments[rating]) : null,
                    CreatedAt = reviewedAt < clock.UtcNow ? reviewedAt : clock.UtcNow,
                    IsVisible = true,
                });
            }
            context.Reviews.AddRange(reviews);

            // The same notifications the hand-written appointments get (AddAppointmentNotifications), plus
            // the approval and suspension notices AdminService sends to the generated doctors
            var appointmentIds = appointments.Select(a => a.Id).ToList();
            var withPeople = await context.Appointments
                .Include(a => a.Slot)
                .Include(a => a.Patient).ThenInclude(p => p.User)
                .Include(a => a.Doctor).ThenInclude(d => d.User)
                .Where(a => appointmentIds.Contains(a.Id))
                .ToListAsync();

            var notifications = new List<Notification>();
            foreach (var appointment in withPeople)
                AddAppointmentNotifications(notifications, appointment, clock);

            var generatedDoctorUserIds = Enumerable.Range(1, GeneratedDoctorCount).Select(GeneratedDoctorId).ToList();
            var approvedDoctorUsers = await context.DoctorProfiles
                .Where(d => d.IsApprovedByAdmin && generatedDoctorUserIds.Contains(d.UserId))
                .Select(d => new { d.UserId, d.User.CreatedAt, d.User.IsActive })
                .ToListAsync();
            foreach (var doctorUser in approvedDoctorUsers)
            {
                notifications.Add(new Notification
                {
                    UserId = doctorUser.UserId,
                    Title = "Profile Approved",
                    Message = "Your doctor profile has been approved. You can now set your schedule and accept appointments.",
                    Type = NotificationType.SystemAlert,
                    IsRead = true,
                    CreatedAt = doctorUser.CreatedAt.AddDays(1),
                });
                if (!doctorUser.IsActive)
                    notifications.Add(new Notification
                    {
                        UserId = doctorUser.UserId,
                        Title = "Profile Suspended",
                        Message = "Your doctor profile has been suspended. Please contact support for more information.",
                        Type = NotificationType.SystemAlert,
                        IsRead = false,
                        CreatedAt = clock.ToUtc(suspendedBefore.ToDateTime(new TimeOnly(10, 0))),
                    });
            }
            context.Notifications.AddRange(notifications);
            await context.SaveChangesAsync();

            // Every doctor's rating cache from the visible reviews, the way DoctorRepo.RefreshRatingCacheAsync does it
            await context.DoctorProfiles.ExecuteUpdateAsync(s => s
                .SetProperty(d => d.AverageRating, d => context.Reviews
                    .Where(r => r.DoctorProfileId == d.Id && r.IsVisible)
                    .Average(r => (double?)r.Rating) ?? 0.0)
                .SetProperty(d => d.TotalReviews, d => context.Reviews
                    .Count(r => r.DoctorProfileId == d.Id && r.IsVisible)));

            await transaction.CommitAsync(CancellationToken.None);
            return (reviews.Count, notifications.Count);
        }, CancellationToken.None);

        logger.LogInformation(
            "Generated demo activity: {Appointments} appointments ({Past} past, {Upcoming} upcoming), {Reviews} reviews, {Notifications} notifications",
            appointments.Count, appointments.Count(a => IsPast(a.Slot)), appointments.Count(a => !IsPast(a.Slot)),
            reviewCount, notificationCount);
    }

    // Past visits: mostly completed, some cancelled or missed, and a few recent ones still waiting
    // for the doctor to complete them
    private static AppointmentStatus PastStatus(Random rng, DateTime start, SeedClock clock)
    {
        var roll = rng.NextDouble();
        if (roll < 0.74) return AppointmentStatus.Completed;
        if (roll < 0.87) return AppointmentStatus.Cancelled;
        if (roll < 0.94)
            return start <= clock.ClinicNow.AddMinutes(-15) ? AppointmentStatus.NoShow : AppointmentStatus.Completed;
        return start >= clock.ClinicNow.AddDays(-3) ? AppointmentStatus.Confirmed : AppointmentStatus.Completed;
    }

    private static T Pick<T>(Random rng, IReadOnlyList<T> items) => items[rng.Next(items.Count)];

    // ── Generated data: names, places, schedules, texts ───────────────────────

    private static readonly string[] MaleFirstNames =
    [
        "Ahmed", "Mohamed", "Mahmoud", "Mostafa", "Omar", "Youssef", "Karim", "Khaled", "Hany", "Sherif",
        "Tamer", "Amr", "Hossam", "Islam", "Ibrahim", "Hassan", "Hussein", "Ali", "Mazen", "Ziad",
        "Adham", "Bassem", "Walid", "Ehab", "Ayman", "Samir", "Nabil", "Fady", "Wael", "Ashraf",
    ];

    private static readonly string[] FemaleFirstNames =
    [
        "Fatma", "Aya", "Mariam", "Nour", "Salma", "Yasmin", "Hana", "Dina", "Rania", "Mona",
        "Heba", "Nadia", "Laila", "Reem", "Shaimaa", "Esraa", "Doaa", "Amira", "Ghada", "Sara",
        "Nada", "Mai", "Rana", "Habiba", "Malak", "Farida", "Jana", "Nermeen", "Samar", "Eman",
    ];

    private static readonly string[] LastNames =
    [
        "Hassan", "Mahmoud", "Abdelrahman", "Saleh", "Fawzy", "Gamal", "Helmy", "Ragab", "Shaker", "Mansour",
        "Zaki", "Naguib", "Farouk", "Soliman", "Abdallah", "El-Sharkawy", "El-Masry", "Kamel", "Sabry", "Fathy",
        "Lotfy", "Ashour", "Hegazy", "Morsy", "Barakat", "Darwish", "Shalaby", "Tawfik", "Ramadan", "Abbas",
        "Nasser", "Fouad", "Wahba", "Hamdy", "Rizk", "Sobhy", "Amin", "Badr", "Khalil", "Osman",
    ];

    private static readonly (string Name, string[] Districts)[] Cities =
    [
        ("Cairo", ["Nasr City", "Heliopolis", "Maadi", "Zamalek", "New Cairo", "Shubra"]),
        ("Giza", ["Dokki", "Mohandessin", "Haram", "Sheikh Zayed", "6th of October"]),
        ("Alexandria", ["Smouha", "Sidi Gaber", "Roushdy", "Miami", "Gleem"]),
        ("Mansoura", ["Toriel", "El-Mashaya", "Gomhoreya District"]),
        ("Tanta", ["El-Bahr", "Saeed District", "El-Gaish"]),
        ("Assiut", ["Gomhoreya District", "El-Hamraa", "University District"]),
        ("Zagazig", ["El-Qawmeya", "El-Montazah", "Downtown"]),
        ("Ismailia", ["Sheikh Zayed District", "El-Shohadaa", "Downtown"]),
    ];

    private static readonly string[] Streets =
    [
        "El-Tahrir Street", "El-Gomhoreya Street", "El-Nasr Road", "Port Said Street", "El-Galaa Street",
        "Abbas El-Akkad Street", "Mostafa El-Nahas Street", "El-Thawra Street", "Salah Salem Street", "El-Horreya Road",
    ];

    // One shift per weekday (the hand-written doctors use split shifts on the same day)
    private static readonly (DayOfWeek Day, TimeOnly Start, TimeOnly End, int SlotMinutes)[][] ScheduleTemplates =
    [
        Shifts(9, 14, 30, DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday),
        Shifts(10, 16, 20, DayOfWeek.Saturday, DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday),
        Shifts(16, 21, 15, DayOfWeek.Sunday, DayOfWeek.Tuesday, DayOfWeek.Thursday),
        [.. Shifts(8, 12, 30, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday),
         .. Shifts(11, 15, 30, DayOfWeek.Saturday)],
    ];

    private static (DayOfWeek Day, TimeOnly Start, TimeOnly End, int SlotMinutes)[] Shifts(
        int startHour, int endHour, int slotMinutes, params DayOfWeek[] days) =>
        [.. days.Select(day => (day, new TimeOnly(startHour, 0), new TimeOnly(endHour, 0), slotMinutes))];

    private static readonly string[] Allergies =
        ["Penicillin", "Sulfa drugs", "Aspirin", "Peanuts", "Shellfish", "Latex", "Dust mites", "Pollen", "Ibuprofen"];

    private static readonly string[] ChronicConditions =
        ["Hypertension", "Type 2 diabetes", "Asthma", "Hypothyroidism", "Migraine", "GERD", "Osteoarthritis", "Anemia"];

    private static readonly Dictionary<string, string> SpecialtyFocus = new()
    {
        ["Cardiology"] = "Cardiologist focused on hypertension, heart failure and preventive cardiac care.",
        ["Dermatology"] = "Dermatologist treating acne, eczema, psoriasis and pigmentation.",
        ["Orthopedics"] = "Orthopedic surgeon for sports injuries, joint pain and back problems.",
        ["Pediatrics"] = "Pediatrician caring for newborns, children and teenagers, including vaccinations.",
        ["Neurology"] = "Neurologist treating migraine, epilepsy, neuropathy and stroke follow-up.",
        ["Gynecology"] = "Gynecologist for women's health, pregnancy follow-up and hormonal disorders.",
        ["Ophthalmology"] = "Ophthalmologist for vision checks, cataract, glaucoma and dry eye.",
        ["ENT"] = "ENT specialist for sinusitis, tonsillitis, hearing loss and allergies.",
        ["Gastroenterology"] = "Gastroenterologist for reflux, IBS, liver disease and endoscopy.",
        ["Psychiatry"] = "Psychiatrist for anxiety, depression, sleep problems and stress management.",
    };

    private static readonly string[] GeneralReasons =
        ["General consultation.", "Follow-up visit.", "Second opinion on a previous diagnosis.", "Review of lab results."];

    private static readonly Dictionary<string, string[]> VisitReasons = new()
    {
        ["Cardiology"] = ["High blood pressure readings at home.", "Chest tightness when climbing stairs.", "Palpitations in the evening.", "Follow-up after an abnormal ECG.", "Shortness of breath and ankle swelling."],
        ["Dermatology"] = ["Persistent acne on the face and back.", "Itchy rash on the arms.", "Hair loss over the last three months.", "Dark spots on the cheeks.", "Mole that changed shape."],
        ["Orthopedics"] = ["Knee pain after running.", "Lower back pain for two weeks.", "Shoulder stiffness and limited movement.", "Ankle sprain follow-up.", "Wrist pain when typing."],
        ["Pediatrics"] = ["Child's routine checkup and vaccination.", "Fever and cough for three days.", "Child has ear pain.", "Poor appetite and weight concerns.", "Skin rash on a toddler."],
        ["Neurology"] = ["Frequent migraines.", "Numbness in the hands.", "Dizziness and balance problems.", "Follow-up on seizure medication.", "Memory complaints."],
        ["Gynecology"] = ["Routine pregnancy follow-up.", "Irregular cycle.", "Annual checkup.", "Pelvic pain.", "Hormonal acne and weight gain."],
        ["Ophthalmology"] = ["Blurred vision when reading.", "Red, itchy eyes.", "Glasses prescription check.", "Floaters in the right eye.", "Glaucoma follow-up."],
        ["ENT"] = ["Blocked nose and sinus pressure.", "Recurring sore throat.", "Ringing in the ears.", "Hearing loss in one ear.", "Snoring and poor sleep."],
        ["Gastroenterology"] = ["Heartburn after meals.", "Bloating and abdominal pain.", "Follow-up on fatty liver.", "Change in bowel habits.", "Nausea for a week."],
        ["Psychiatry"] = ["Trouble sleeping for a month.", "Ongoing anxiety at work.", "Low mood and loss of interest.", "Medication review.", "Panic attacks."],
    };

    private static readonly string[] GeneralNotes =
        ["Examination unremarkable. Reassured the patient. Follow up if symptoms persist.", "Ordered routine blood tests. Review results at the next visit."];

    private static readonly Dictionary<string, string[]> DoctorNotesBySpecialty = new()
    {
        ["Cardiology"] = ["BP 145/92. Started Amlodipine 5 mg daily, low-salt diet. Recheck in 4 weeks.", "ECG normal sinus rhythm. Holter monitor ordered for palpitations.", "Echo shows preserved EF. Continue current medication."],
        ["Dermatology"] = ["Moderate acne. Topical adapalene nightly, gentle cleanser. Review in 8 weeks.", "Contact dermatitis. Hydrocortisone 1% for 7 days, avoid the trigger.", "Benign naevus. No action needed; photo recorded for comparison."],
        ["Orthopedics"] = ["Patellofemoral pain. Physiotherapy 3x/week, ice after exercise.", "Mechanical low back pain. NSAIDs for 5 days, core exercises.", "Grade 1 ankle sprain. RICE protocol and gradual return to sport."],
        ["Pediatrics"] = ["Vaccines given as scheduled. Growth on the 50th percentile.", "Viral URTI. Fluids, paracetamol for fever, return if worse.", "Otitis media. Amoxicillin for 7 days."],
        ["Neurology"] = ["Episodic migraine. Started propranolol 40 mg, headache diary.", "Carpal tunnel suspected. Nerve conduction study ordered.", "Seizure-free for 6 months. Continue levetiracetam."],
        ["Gynecology"] = ["Pregnancy at 20 weeks progressing normally. Anomaly scan booked.", "Irregular cycle. Hormonal panel and ultrasound ordered.", "Annual exam normal. Pap smear taken."],
        ["Ophthalmology"] = ["Mild myopia. New glasses prescription issued.", "Allergic conjunctivitis. Antihistamine drops twice daily.", "IOP stable on current drops."],
        ["ENT"] = ["Acute sinusitis. Nasal steroid spray and saline rinses.", "Recurrent tonsillitis. Discussed tonsillectomy options.", "Wax impaction removed. Hearing improved."],
        ["Gastroenterology"] = ["GERD. Omeprazole 20 mg before breakfast for 8 weeks.", "IBS likely. Low-FODMAP diet trial.", "Fatty liver. Weight loss plan, repeat liver enzymes in 3 months."],
        ["Psychiatry"] = ["Generalised anxiety. Started CBT referral and sertraline 50 mg.", "Insomnia. Sleep hygiene plan, review in 3 weeks.", "Mood improving on current treatment. Continue."],
    };

    private static readonly Dictionary<CancelledBy, string[]> CancellationReasons = new()
    {
        [CancelledBy.Patient] = ["Personal schedule conflict.", "Feeling better, no longer needed.", "Travelling that week.", "Booked a different time instead."],
        [CancelledBy.Doctor] = ["Doctor unavailable due to an emergency.", "Doctor attending a medical conference.", "Clinic closed for maintenance that day."],
        [CancelledBy.Admin] = ["Cancelled at the patient's request by phone.", "Duplicate booking removed by the clinic.", "Clinic holiday."],
    };

    private static readonly Dictionary<int, string[]> ReviewComments = new()
    {
        [5] = ["Excellent doctor, very attentive and clear.", "Explained everything patiently. Highly recommend.", "Quick diagnosis and the treatment worked.", "Very professional and kind. The clinic was well organised."],
        [4] = ["Good experience overall, a short wait.", "Knowledgeable doctor; the clinic was busy.", "Helpful advice, would visit again."],
        [3] = ["Average visit. The doctor was fine but rushed.", "Long wait, but the advice was OK.", "Okay, though I expected more explanation."],
        [2] = ["Waited over an hour and the visit felt rushed.", "The doctor didn't listen to all my concerns."],
        [1] = ["Very disappointing visit.", "Appointment started late and I left without answers."],
    };
}
