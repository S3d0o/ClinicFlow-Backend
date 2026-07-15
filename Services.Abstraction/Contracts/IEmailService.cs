namespace Services.Abstraction.Contracts
{
    public interface IEmailService
    {
        // Auth flow
        Task SendEmailConfirmationAsync(string email, Guid userId, string token);
        Task SendPasswordResetAsync(string email, Guid userId, string token);

        // Appointment flow
        Task SendAppointmentConfirmationAsync(string email, string patientName,
            string doctorName, DateOnly date, TimeOnly time);
        Task SendAppointmentReminderAsync(string email, string patientName,
            string doctorName, DateOnly date, TimeOnly time);
    }
}
