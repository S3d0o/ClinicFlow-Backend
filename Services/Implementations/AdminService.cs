using Domain.Enums;
using Shared.DTOs.Admin;
using Shared.DTOs.Appointment;
using Shared.DTOs.Common;
using Shared.DTOs.Doctor;

namespace Services.Implementations;

public class AdminService(
    IUnitOfWork unitOfWork,
    IMapper mapper,
    ILogger<AdminService> logger,
    IClock clock) : IAdminService
{
    public async Task<Result<PagedList<DoctorResponse>>> GetPendingDoctorsAsync(PendingDoctorsFilterRequest filter, CancellationToken ct)
    {
        var (pendingDoctors, totalCount) = await unitOfWork.Doctors.GetPendingApprovalsAsync(filter.PageNumber, filter.PageSize, ct);
        var result = mapper.Map<List<DoctorResponse>>(pendingDoctors);
        logger.LogInformation("Retrieved {Count} of {TotalCount} pending doctors for approval.", result.Count, totalCount);
        return new PagedList<DoctorResponse>(result, totalCount);
    }

    public async Task<Result> ApproveDoctorAsync(int doctorProfileId, CancellationToken ct)
    {
        var doctor = await unitOfWork.Doctors.GetByIdAsync(doctorProfileId, ct);
        if (doctor is null)
        {
            logger.LogWarning("Attempted to approve non-existent doctor {DoctorProfileId}.", doctorProfileId);
            return DoctorErrors.NotFound(doctorProfileId);
        }

        if (doctor.IsApprovedByAdmin)
        {
            logger.LogWarning("Doctor {DoctorProfileId} is already approved.", doctorProfileId);
            return AdminErrors.DoctorAlreadyApproved;
        }

        doctor.IsApprovedByAdmin = true;
        unitOfWork.Outbox.Add(new DoctorApproved(doctor.UserId)); // its handler notifies the doctor
        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation("Doctor {DoctorProfileId} approved successfully.", doctorProfileId);
        return Result.Ok();
    }

    public async Task<Result> SuspendDoctorAsync(int doctorProfileId, CancellationToken ct)
    {
        // GetByIdAsync now includes User navigation
        var doctor = await unitOfWork.Doctors.GetByIdAsync(doctorProfileId, ct);
        if (doctor is null)
        {
            logger.LogWarning("Attempted to suspend non-existent doctor {DoctorProfileId}.", doctorProfileId);
            return DoctorErrors.NotFound(doctorProfileId);
        }

        if (!doctor.User.IsActive)
        {
            logger.LogWarning("Doctor {DoctorProfileId} is already suspended.", doctorProfileId);
            return AdminErrors.DoctorAlreadySuspended;
        }

        doctor.User.IsActive = false;
        unitOfWork.Outbox.Add(new DoctorSuspended(doctor.UserId));
        await unitOfWork.SaveChangesAsync(ct);

        // End existing sessions: refresh also rejects inactive users, and revoking stops
        // further refreshes immediately. Access tokens stay valid until they expire (15 min).
        await unitOfWork.RefreshTokens.RevokeAllActiveAsync(doctor.UserId, "Account suspended", null, clock.UtcNow, ct);

        logger.LogInformation("Doctor {DoctorProfileId} suspended successfully.", doctorProfileId);
        return Result.Ok();
    }

    public async Task<Result<OverviewStatsResponse>> GetOverviewStatsAsync(CancellationToken ct)
    {
        var (totalDoctors, totalPatients, totalAppointments,
             totalSpecialties, pendingApprovals, totalReviews)
            = await unitOfWork.Admins.GetOverviewStatsAsync(ct);

        logger.LogInformation("Overview stats retrieved successfully.");

        return new OverviewStatsResponse(
            totalDoctors,
            totalPatients,
            totalAppointments,
            totalSpecialties,
            pendingApprovals,
            totalReviews);
    }

    public async Task<Result<AppointmentStatsResponse>> GetAppointmentStatsAsync(CancellationToken ct)
    {
        var (pending, confirmed, completed, cancelled, noShow)
            = await unitOfWork.Appointments.GetStatsAsync(ct);

        logger.LogInformation("Appointment stats retrieved successfully.");

        return new AppointmentStatsResponse(
            pending,
            confirmed,
            completed,
            cancelled,
            noShow);
    }
}
