namespace Shared.DTOs.Appointment
{
    public record AppointmentStatsResponse(
    int Pending,
    int Confirmed,
    int Completed,
    int Cancelled,
    int NoShow);
}
