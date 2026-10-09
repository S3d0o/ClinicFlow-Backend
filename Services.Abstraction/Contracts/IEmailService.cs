namespace Services.Abstraction.Contracts
{
    public interface IEmailService
    {
        // Auth flow
        Task SendEmailConfirmationAsync(string email, Guid userId, string token, CancellationToken ct = default);
        Task SendPasswordResetAsync(string email, Guid userId, string token, CancellationToken ct = default);

        // Appointment flow
        Task SendAppointmentConfirmationAsync(string email, string patientName,
            string doctorName, DateOnly date, TimeOnly time, CancellationToken ct = default);
        Task SendAppointmentReminderAsync(string email, string patientName,
            string doctorName, DateOnly date, TimeOnly time, CancellationToken ct = default);
    }
}
