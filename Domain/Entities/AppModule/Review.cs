using System.ComponentModel.DataAnnotations;

namespace Domain.Entities.AppModule
{
    public class Review : BaseEntity<int>
    {
        public int AppointmentId { get; set; }   // unique constraint in EF config
        public int PatientProfileId { get; set; }
        public int DoctorProfileId { get; set; }

            public int Rating { get; set; }          // 1–5, validated in service layer too
            public string? Comment { get; set; }     // max 300 chars
            public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
            public bool IsVisible { get; set; } = true; // admin can hide without deleting

        // Navigation
        public Appointment Appointment { get; set; } = null!;
        public PatientProfile Patient { get; set; } = null!;
        public DoctorProfile Doctor { get; set; } = null!;
    }
}