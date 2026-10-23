namespace Domain.Interfaces.IRepositories
{
    // Loads appointments for commands. Reads for responses are IAppointmentQueries (CQRS-lite)
    public interface IAppointmentRepo
    {
        // Tracked, with slot (and its schedule), doctor and patient
        Task<Appointment?> GetByIdAsync(int id, CancellationToken ct);
        // Tracked, without navigations: the reminder job only marks them (Appointment.QueueReminder)
        Task<IReadOnlyList<Appointment>> GetAppointmentsNeedingReminderAsync(DateTime utcNow, CancellationToken ct);
        Task<(int Pending, int Confirmed, int Completed, int Cancelled, int NoShow)> GetStatsAsync(CancellationToken ct);

        // Write
        Task AddAsync(Appointment appointment, CancellationToken ct);
    }
}
