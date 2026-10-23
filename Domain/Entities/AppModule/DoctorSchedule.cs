namespace Domain.Entities.AppModule
{
    public class DoctorSchedule : BaseEntity<int>, IOrganizationOwned, IBranchOwned
    {
        public int OrganizationId { get; set; }
        public int BranchId { get; set; }

        public int DoctorProfileId { get; set; }

        public DayOfWeek DayOfWeek { get; set; }     // 0 = Sunday … 6 = Saturday
        public TimeOnly StartTime { get; set; }       // e.g. 09:00
        public TimeOnly EndTime { get; set; }         // e.g. 17:00
        public int SlotDurationMinutes { get; set; } = 30;
        public bool IsActive { get; set; } = true;

        // Navigation
        public DoctorProfile DoctorProfile { get; set; } = null!;
        public ICollection<AppointmentSlot> Slots { get; set; } = [];

        // A freed slot may be offered to patients again only while the schedule is active and the slot
        // lies within its current hours; otherwise it is blocked (REVIEW.md H4)
        public bool Covers(AppointmentSlot slot)
            => IsActive && slot.StartTime >= StartTime && slot.EndTime <= EndTime;

    }
}
