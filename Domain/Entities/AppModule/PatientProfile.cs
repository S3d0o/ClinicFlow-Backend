using ClinicFlow.Domain.Enums;
using Domain.Entities.IdentityModule;

namespace Domain.Entities.AppModule
{
    public class PatientProfile : BaseEntity<int>
    {
        public Guid UserId { get; set; } = Guid.Empty;

        public BloodType? BloodType { get; set; }
        public string? Allergies { get; set; }
        public string? ChronicConditions { get; set; }
        public string? EmergencyContactName { get; set; }
        public string? EmergencyContactPhone { get; set; }

        // Navigation
        public ApplicationUser User { get; set; } = null!;
        public ICollection<Appointment> Appointments { get; set; } = [];
        public ICollection<Review> Reviews { get; set; } = [];
    }
}
