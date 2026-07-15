using ClinicFlow.Domain.Enums;
using Domain.Enums;

namespace Shared.DTOs.Appointment
{
    public record AppointmentResponse
    {
        public int Id { get; init; }
        public int SlotId { get; init; }
        public int DoctorProfileId { get; init; }
        public int PatientProfileId { get; init; }
        public string DoctorName { get; init; } = string.Empty;
        public string PatientName { get; init; } = string.Empty;
        public DateOnly AppointmentDate { get; init; }
        public TimeOnly StartTime { get; init; }
        public TimeOnly EndTime { get; init; }
        public AppointmentStatus Status { get; init; }
        public string? ReasonForVisit { get; init; }
        public string? DoctorNotes { get; init; }
        public DateTime BookedAt { get; init; }
        public DateTime? CancelledAt { get; init; }
        public string? CancellationReason { get; init; }
        public CancelledBy? CancelledBy { get; init; }
        public bool HasReview { get; init; }
    }
}
