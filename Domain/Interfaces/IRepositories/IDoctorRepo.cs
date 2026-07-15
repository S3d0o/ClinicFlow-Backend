using Domain.Parameters;

namespace Domain.Interfaces.IRepositories
{
    public interface IDoctorRepo
    {
        // Queries
        Task<DoctorProfile?> GetByIdAsync(int id, CancellationToken ct);
        Task<DoctorProfile?> GetDetailedByIdAsync(int id, CancellationToken ct); // includes User + Specialty
        Task<DoctorProfile?> GetByUserIdAsync(Guid userId, CancellationToken ct);
        Task<(IReadOnlyList<DoctorProfile> Doctors, int TotalCount)> GetApprovedPagedAsync(DoctorFilterParams filters, CancellationToken ct);
        Task<IReadOnlyList<DoctorProfile>> GetPendingApprovalsAsync(CancellationToken ct);
        Task<bool> ExistsAsync(int id, CancellationToken ct);

        // Schedule
        Task<IReadOnlyList<DoctorSchedule>> GetSchedulesAsync(int doctorProfileId, CancellationToken ct);
        Task<DoctorSchedule?> GetScheduleByIdAsync(int scheduleId, CancellationToken ct);
        void AddSchedule(DoctorSchedule schedule);
        void UpdateSchedule(DoctorSchedule schedule);
        void DeleteSchedule(DoctorSchedule schedule);
        public Task<DoctorSchedule?> GetScheduleByUserIdAndDayAsync(Guid userId, DayOfWeek day, CancellationToken ct);

        // Write
        Task AddAsync(DoctorProfile doctor, CancellationToken ct);
        void Update(DoctorProfile doctor);
        void UpdateRatingCache(DoctorProfile doctor);
    }
}
