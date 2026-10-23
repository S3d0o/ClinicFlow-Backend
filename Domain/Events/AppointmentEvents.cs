using ClinicFlow.Domain.Enums;

namespace Domain.Events
{
    // Raised by Appointment's transitions (docs/v2/adr/0004 decision 5). AppointmentBooked is the exception: the
    // appointment has no id until it is inserted, so the booking service adds it after the insert, in the same
    // transaction
    public record AppointmentBooked(int AppointmentId) : IDomainEvent;
    public record AppointmentCancelled(int AppointmentId, CancelledBy By) : IDomainEvent;
    public record AppointmentCompleted(int AppointmentId) : IDomainEvent;
    public record AppointmentMarkedNoShow(int AppointmentId) : IDomainEvent;
    public record AppointmentNoShowReverted(int AppointmentId) : IDomainEvent;
    public record PatientCheckedIn(int AppointmentId) : IDomainEvent;
    public record AppointmentStarted(int AppointmentId) : IDomainEvent;
    public record AppointmentRescheduled(int AppointmentId) : IDomainEvent; // the replacement is RescheduledTo
    public record AppointmentReminderDue(int AppointmentId) : IDomainEvent;
}
