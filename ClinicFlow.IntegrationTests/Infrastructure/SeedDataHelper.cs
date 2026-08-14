using Domain.Entities.AppModule;
using Domain.Entities.IdentityModule;
using Domain.Enums;
using Microsoft.AspNetCore.Identity;
using Persistence.AppData;
using Shared.Enums;
using ClinicFlow.Domain.Enums;

namespace ClinicFlow.IntegrationTests.Infrastructure
{
    public static class SeedDataHelper
    {
        public static async Task<(Guid patientUserId, Guid doctorUserId, int slotId, int appointmentId)>
            SeedBaseAppointmentAsync(ClinicDbContext db, AppointmentStatus status = AppointmentStatus.Confirmed, string suffix = "")
        {
            var hasher = new PasswordHasher<ApplicationUser>();

            // Reuse existing specialty if it already exists
            var specialty = db.Specialties.FirstOrDefault(s => s.Name == "General Practice")
                ?? new Specialty { Name = "General Practice", IsActive = true };

            if (specialty.Id == 0)
            {
                db.Specialties.Add(specialty);
                await db.SaveChangesAsync();
            }

            var doctorUser = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                FirstName = "Test",
                LastName = "Doctor",
                UserName = $"doctor{suffix}@test.com",
                NormalizedUserName = $"DOCTOR{suffix}@TEST.COM",
                Email = $"doctor{suffix}@test.com",
                NormalizedEmail = $"DOCTOR{suffix}@TEST.COM",
                EmailConfirmed = true,
                Gender = Gender.Male,
                SecurityStamp = Guid.NewGuid().ToString(),
                IsActive = true
            };
            doctorUser.PasswordHash = hasher.HashPassword(doctorUser, "Test@1234!");
            db.Users.Add(doctorUser);
            await db.SaveChangesAsync();

            var doctorProfile = new DoctorProfile
            {
                UserId = doctorUser.Id,
                SpecialtyId = specialty.Id,
                YearsOfExperience = 5,
                ConsultationFee = 200,
                IsApprovedByAdmin = true
            };
            db.DoctorProfiles.Add(doctorProfile);
            await db.SaveChangesAsync();

            var schedule = new DoctorSchedule
            {
                DoctorProfileId = doctorProfile.Id,
                DayOfWeek = DayOfWeek.Monday,
                StartTime = new TimeOnly(9, 0),
                EndTime = new TimeOnly(17, 0),
                SlotDurationMinutes = 30,
                IsActive = true
            };
            db.DoctorSchedules.Add(schedule);
            await db.SaveChangesAsync();

            var patientUser = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                FirstName = "Test",
                LastName = "Patient",
                UserName = $"patient{suffix}@test.com",
                NormalizedUserName = $"PATIENT{suffix}@TEST.COM",
                Email = $"patient{suffix}@test.com",
                NormalizedEmail = $"PATIENT{suffix}@TEST.COM",
                EmailConfirmed = true,
                Gender = Gender.Female,
                SecurityStamp = Guid.NewGuid().ToString(),
                IsActive = true
            };
            patientUser.PasswordHash = hasher.HashPassword(patientUser, "Test@1234!");
            db.Users.Add(patientUser);
            await db.SaveChangesAsync();

            var patientProfile = new PatientProfile { UserId = patientUser.Id };
            db.PatientProfiles.Add(patientProfile);
            await db.SaveChangesAsync();

            var slot = new AppointmentSlot
            {
                DoctorProfileId = doctorProfile.Id,
                DoctorScheduleId = schedule.Id,
                Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3)),
                StartTime = new TimeOnly(10, 0),
                EndTime = new TimeOnly(10, 30),
                Status = SlotStatus.Available
            };
            db.AppointmentSlots.Add(slot);
            await db.SaveChangesAsync();

            var appointment = new Appointment
            {
                PatientProfileId = patientProfile.Id,
                DoctorProfileId = doctorProfile.Id,
                SlotId = slot.Id,
                Status = status  // ← caller controls this
            };
            db.Appointments.Add(appointment);
            await db.SaveChangesAsync();

            return (patientUser.Id, doctorUser.Id, slot.Id, appointment.Id);
        }
    }
}