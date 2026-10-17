namespace Domain.Interfaces.IRepositories
{
    public interface ISlotRepo
    {
        // Queries
        Task<AppointmentSlot?> GetByIdAsync(int id, CancellationToken ct);
        Task<IReadOnlyList<AppointmentSlot>> GetAvailableByDoctorAndDateAsync(int doctorProfileId, DateOnly date, CancellationToken ct);
        Task<bool> IsAvailableAsync(int slotId, CancellationToken ct);
        // Booked slots from today on: appointments a schedule change must not silently drop
        Task<bool> HasUpcomingBookingsAsync(int scheduleId, CancellationToken ct);
        Task<bool> AnyForScheduleAsync(int scheduleId, CancellationToken ct);
        // Every slot the doctor already has in the range, so generation can skip times that overlap them
        Task<List<(DateOnly Date, TimeOnly StartTime, TimeOnly EndTime)>> GetSlotTimesAsync(int doctorProfileId, DateOnly from, DateOnly to, CancellationToken ct);

        // Write
        Task AddRangeAsync(IEnumerable<AppointmentSlot> slots, CancellationToken ct);
        Task<int> SetStatusFromAvailableToBookedAsync(int slotId, CancellationToken ct);
        void Update(AppointmentSlot slot);
        void Block(AppointmentSlot slot);
        Task DeleteFutureAvailableByScheduleAsync(int scheduleId, CancellationToken ct);
        // Deletes the schedule's slots that no appointment (of any status) points to
        Task DeleteUnusedByScheduleAsync(int scheduleId, CancellationToken ct);
    }
}
