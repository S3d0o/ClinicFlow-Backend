
using ClinicFlow.Domain.Enums;

namespace Domain.Entities.AppModule
{
    public class Appointment : BaseEntity<int>
    {
        public int SlotId { get; set; } 
        public int PatientProfileId { get; set; } 
        public int DoctorProfileId { get; set; } // denormalized — avoids joining through Slot every query

        public AppointmentStatus Status { get; set; } = AppointmentStatus.Pending;

        public string? ReasonForVisit { get; set; }  // patient sets this at booking time
        public string? DoctorNotes { get; set; }      // only the owning doctor can write this

        public DateTime BookedAt { get; set; } = DateTime.UtcNow;

        // All nullable — only populated when the appointment is cancelled
        public DateTime? CancelledAt { get; set; }
        public string? CancellationReason { get; set; }
        public CancelledBy? CancelledBy { get; set; }

        // Background job sets this after sending the reminder notification
        public DateTime? ReminderSentAt { get; set; }

        // Navigation
        public AppointmentSlot Slot { get; set; } = null!;
        public PatientProfile Patient { get; set; } = null!;
        public DoctorProfile Doctor { get; set; } = null!;
        public Review? Review { get; set; } // null until patient submits one
    }
}
