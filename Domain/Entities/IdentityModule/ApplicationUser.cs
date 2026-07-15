namespace Domain.Entities.IdentityModule
{
    public class ApplicationUser : IdentityUser<Guid>
    {
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;

        public string? ProfilePictureUrl { get; set; }
        public Gender Gender { get; set; }
        public DateOnly? DateOfBirth { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public bool IsActive { get; set; } = true;

        // Security and authentication
        public ICollection<RefreshToken> RefreshTokens { get; set; } = [];

        // Navigation
        public DoctorProfile? DoctorProfile { get; set; }
        public PatientProfile? PatientProfile { get; set; }
        public ICollection<Notification> Notifications { get; set; } = [];
    }
}
