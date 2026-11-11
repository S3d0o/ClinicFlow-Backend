namespace Domain.Entities.IdentityModule
{
    public class ApplicationUser : IdentityUser<Guid>
    {
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;

        public string? ProfilePictureUrl { get; set; }
        public Gender Gender { get; set; }
        public DateOnly? DateOfBirth { get; set; }

        public DateTime CreatedAt { get; set; }
        public bool IsActive { get; set; } = true;

        // Every email and phone message to the user is written in it (FR-ACC-06, R10)
        public Language PreferredLanguage { get; set; } = Language.Ar;
        // The channel for booking confirmations and reminders (FR-NTF-04)
        public NotificationChannel NotificationChannel { get; set; } = NotificationChannel.Both;

        // Whether booking confirmations and reminders go to the user's email: only to a confirmed address, and not
        // when they chose phone messages only. Account emails (confirmation, password reset) don't ask
        public bool AcceptsEmail()
            => !string.IsNullOrEmpty(Email) && EmailConfirmed && NotificationChannel != NotificationChannel.Phone;

        // Security and authentication
        public ICollection<RefreshToken> RefreshTokens { get; set; } = [];

        // Navigation
        public DoctorProfile? DoctorProfile { get; set; }
        public PatientProfile? PatientProfile { get; set; }
        public ICollection<Notification> Notifications { get; set; } = [];
    }
}
