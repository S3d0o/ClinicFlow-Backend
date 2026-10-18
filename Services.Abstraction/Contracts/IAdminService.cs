using Shared.DTOs.Admin;
using Shared.DTOs.Appointment;
using Shared.DTOs.Common;
using Shared.DTOs.Doctor;

namespace Services.Abstraction.Contracts
{
    public interface IAdminService
    {
        Task<Result<PagedList<DoctorResponse>>> GetPendingDoctorsAsync(PendingDoctorsFilterRequest filter, CancellationToken ct);
        Task<Result> ApproveDoctorAsync(int doctorProfileId, CancellationToken ct);
        Task<Result> SuspendDoctorAsync(int doctorProfileId, CancellationToken ct);
        Task<Result<OverviewStatsResponse>> GetOverviewStatsAsync(CancellationToken ct);
        Task<Result<AppointmentStatsResponse>> GetAppointmentStatsAsync(CancellationToken ct);
    }
}
