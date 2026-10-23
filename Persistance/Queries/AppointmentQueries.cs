using System.Linq.Expressions;
using Domain.Errors;
using Domain.ResultPattern;
using Microsoft.Extensions.Logging;
using Persistence.Repositories;
using Services.Abstraction.Contracts;
using Shared.DTOs.Appointment;
using Shared.Errors;

namespace Persistence.Queries
{
    // Appointment reads (CQRS-lite): untracked, and projected in SQL to exactly the response's columns
    public class AppointmentQueries(ClinicDbContext context, ILogger<AppointmentQueries> logger) : IAppointmentQueries
    {
        public async Task<Result<AppointmentResponse>> GetAppointmentAsync(int appointmentId, Guid userId, string role, CancellationToken ct)
        {
            var owners = await context.Appointments
                .AsNoTracking()
                .Where(a => a.Id == appointmentId)
                .Select(a => new { PatientUserId = a.Patient.UserId, DoctorUserId = a.Doctor.UserId })
                .FirstOrDefaultAsync(ct);
            if (owners == null)
            {
                logger.LogWarning("Appointment not found for ID: {AppointmentId}", appointmentId);
                return AppointmentErrors.NotFound(appointmentId);
            }

            // Appointments contain medical data: only the owning patient, the owning doctor or an admin may read them
            var canRead = role switch
            {
                Roles.Admin => true,
                Roles.Patient => owners.PatientUserId == userId,
                Roles.Doctor => owners.DoctorUserId == userId,
                _ => false
            };
            if (!canRead)
            {
                logger.LogWarning("User {UserId} ({Role}) attempted to read appointment {AppointmentId} they do not own", userId, role, appointmentId);
                return AppointmentErrors.Forbidden;
            }

            return await context.Appointments
                .AsNoTracking()
                .Where(a => a.Id == appointmentId)
                .Select(ToResponse)
                .FirstAsync(ct);
        }

        public async Task<Result<List<AppointmentResponse>>> GetPatientAppointmentsAsync(Guid patientUserId, AppointmentFilterRequest request, CancellationToken ct)
        {
            if (!await context.PatientProfiles.AnyAsync(p => p.UserId == patientUserId, ct))
            {
                logger.LogWarning("Patient profile not found for user ID: {UserId}", patientUserId);
                return PatientErrors.ProfileNotFound(patientUserId);
            }

            return await ListAsync(context.Appointments.Where(a => a.Patient.UserId == patientUserId), request, ct);
        }

        public async Task<Result<List<AppointmentResponse>>> GetDoctorAppointmentsAsync(Guid doctorUserId, AppointmentFilterRequest request, CancellationToken ct)
        {
            if (!await context.DoctorProfiles.AnyAsync(d => d.UserId == doctorUserId, ct))
            {
                logger.LogWarning("Doctor profile not found for user ID: {UserId}", doctorUserId);
                return DoctorErrors.ProfileNotFound(doctorUserId);
            }

            return await ListAsync(context.Appointments.Where(a => a.Doctor.UserId == doctorUserId), request, ct);
        }

        // Newest visit first; the id breaks ties so paging stays stable
        private static async Task<List<AppointmentResponse>> ListAsync(IQueryable<Appointment> appointments, AppointmentFilterRequest request, CancellationToken ct)
        {
            if (request.Status is { } status)
                appointments = appointments.Where(a => a.Status == status);

            return await appointments
                .AsNoTracking()
                .OrderByDescending(a => a.Slot.Date)
                    .ThenByDescending(a => a.Slot.StartTime)
                    .ThenByDescending(a => a.Id)
                .Page(request.PageNumber, request.PageSize)
                .Select(ToResponse)
                .ToListAsync(ct);
        }

        private static readonly Expression<Func<Appointment, AppointmentResponse>> ToResponse = a => new AppointmentResponse
        {
            Id = a.Id,
            SlotId = a.SlotId,
            DoctorProfileId = a.DoctorProfileId,
            PatientProfileId = a.PatientProfileId,
            DoctorName = a.Doctor.User.FirstName + " " + a.Doctor.User.LastName,
            PatientName = a.Patient.User.FirstName + " " + a.Patient.User.LastName,
            AppointmentDate = a.Slot.Date,
            StartTime = a.Slot.StartTime,
            EndTime = a.Slot.EndTime,
            Status = a.Status,
            ReasonForVisit = a.ReasonForVisit,
            DoctorNotes = a.DoctorNotes,
            BookedAt = a.BookedAt,
            CancelledAt = a.CancelledAt,
            CancellationReason = a.CancellationReason,
            CancelledBy = a.CancelledBy,
            HasReview = a.Review != null,
        };
    }
}
