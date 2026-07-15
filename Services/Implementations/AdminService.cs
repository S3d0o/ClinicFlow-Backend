using Domain.Enums;
using Shared.DTOs.Admin;
using Shared.DTOs.Appointment;
using Shared.DTOs.Doctor;
using Shared.DTOs.Notification;

namespace Services.Implementations;

public class AdminService(
    IUnitOfWork unitOfWork,
    IMapper mapper,
    ILogger<AdminService> logger,
    INotificationService notificationService) : IAdminService
{
    public async Task<Result<List<DoctorResponse>>> GetPendingDoctorsAsync(CancellationToken ct)
    {
        var pendingDoctors = await unitOfWork.Doctors.GetPendingApprovalsAsync(ct);
        var result = mapper.Map<List<DoctorResponse>>(pendingDoctors);
        logger.LogInformation("Retrieved {Count} pending doctors for approval.", result.Count);
        return result;
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
        await unitOfWork.SaveChangesAsync(ct);

        await notificationService.CreateAsync(new CreateNotificationRequest(
            UserId: doctor.UserId,
            Title: "Profile Approved",
            Message: "Your doctor profile has been approved. You can now set your schedule and accept appointments.",
            Type: NotificationType.SystemAlert,
            RelatedEntityId: null), ct);

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
        await unitOfWork.SaveChangesAsync(ct);

        await notificationService.CreateAsync(new CreateNotificationRequest(
            UserId: doctor.UserId,
            Title: "Profile Suspended",
            Message: "Your doctor profile has been suspended. Please contact support for more information.",
            Type: NotificationType.SystemAlert,
            RelatedEntityId: null), ct);

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