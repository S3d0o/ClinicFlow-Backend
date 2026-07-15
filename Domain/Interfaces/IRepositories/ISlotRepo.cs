namespace Domain.Interfaces.IRepositories
{
    public interface ISlotRepo
    {
        // Queries
        Task<AppointmentSlot?> GetByIdAsync(int id, CancellationToken ct);
        Task<IReadOnlyList<AppointmentSlot>> GetAvailableByDoctorAndDateAsync(int doctorProfileId, DateOnly date, CancellationToken ct);
        Task<bool> IsAvailableAsync(int slotId, CancellationToken ct);

        // Write
        Task AddRangeAsync(IEnumerable<AppointmentSlot> slots, CancellationToken ct);
        Task<int> SetStatusFromAvailableToBookedAsync(int slotId, CancellationToken ct);
        void Update(AppointmentSlot slot);
        void Block(AppointmentSlot slot);
        Task DeleteFutureAvailableByScheduleAsync(int scheduleId, CancellationToken ct);
    }
}
