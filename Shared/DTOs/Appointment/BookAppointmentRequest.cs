namespace Shared.DTOs.Appointment
{
    public record BookAppointmentRequest(
        int SlotId,
        string? ReasonForVisit
    );


}
