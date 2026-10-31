using Domain.Entities.IdentityModule;

namespace Domain.Entities.AppModule
{
    public class DoctorProfile : BaseEntity<int>, IOrganizationOwned
    {
        public int OrganizationId { get; set; }

        public Guid UserId { get; set; } = Guid.Empty;
        public int SpecialtyId { get; set; }
        public int? StaffMemberId { get; set; } // the doctor as a staff member (P1 PR 4); null only before the backfill

        public string? Bio { get; set; }             // max 500 chars — enforced in EF config
        public int YearsOfExperience { get; set; }
        public decimal ConsultationFee { get; set; } // in EGP

        // Recomputed every time a review is submitted or deleted
        public double AverageRating { get; set; } = 0.0;
        public int TotalReviews { get; set; } = 0;

        public bool IsApprovedByAdmin { get; set; } = false;

        public string? ClinicAddress { get; set; }
        public string? ClinicCity { get; set; }

        // Navigation
        public ApplicationUser User { get; set; } = null!;
        public Specialty Specialty { get; set; } = null!;
        public ICollection<DoctorSchedule> Schedules { get; set; } = [];
        public ICollection<AppointmentSlot> Slots { get; set; } = [];
        public ICollection<Appointment> Appointments { get; set; } = [];
        public ICollection<Review> Reviews { get; set; } = [];

    }
}
