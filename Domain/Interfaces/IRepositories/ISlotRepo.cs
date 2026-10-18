namespace Domain.Interfaces.IRepositories
{
    public interface ISlotRepo
    {
        // Queries
        Task<AppointmentSlot?> GetByIdAsync(int id, CancellationToken ct);
        Task<IReadOnlyList<AppointmentSlot>> GetAvailableByDoctorAndDateAsync(int doctorProfileId, DateOnly date, CancellationToken ct);
        // Booked slots from today (the clinic's date) on: appointments a schedule change must not silently drop
        Task<bool> HasUpcomingBookingsAsync(int scheduleId, DateOnly today, CancellationToken ct);
        Task<bool> AnyForScheduleAsync(int scheduleId, CancellationToken ct);
        // Every slot the doctor already has in the range, so generation can skip times that overlap them
        Task<List<(DateOnly Date, TimeOnly StartTime, TimeOnly EndTime)>> GetSlotTimesAsync(int doctorProfileId, DateOnly from, DateOnly to, CancellationToken ct);

        // Write
        Task AddRangeAsync(IEnumerable<AppointmentSlot> slots, CancellationToken ct);
        Task<int> SetStatusFromAvailableToBookedAsync(int slotId, CancellationToken ct);
        Task DeleteFutureAvailableByScheduleAsync(int scheduleId, DateOnly today, CancellationToken ct);
        // Free slots kept from today on (a cancelled appointment points to them, so they can't be deleted) are
        // blocked when the schedule no longer covers them, and reopened when it covers them again (REVIEW.md H4)
        Task SyncKeptSlotsWithScheduleAsync(DoctorSchedule schedule, DateOnly today, CancellationToken ct);
        // Deletes the schedule's slots that no appointment (of any status) points to
        Task DeleteUnusedByScheduleAsync(int scheduleId, CancellationToken ct);
    }
}
