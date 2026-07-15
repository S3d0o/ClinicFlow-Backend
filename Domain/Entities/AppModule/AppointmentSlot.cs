using ClinicFlow.Domain.Enums;

namespace Domain.Entities.AppModule
{
    public class AppointmentSlot : BaseEntity<int>
    {
        public int DoctorProfileId { get; set; }
        public int DoctorScheduleId { get; set; }  // which schedule template generated this slot

        public DateOnly Date { get; set; }
        public TimeOnly StartTime { get; set; }
        public TimeOnly EndTime { get; set; }
        public SlotStatus Status { get; set; } = SlotStatus.Available;

        // Navigation
        public DoctorProfile DoctorProfile { get; set; } = null!;
        public DoctorSchedule DoctorSchedule { get; set; } = null!;
        public ICollection<Appointment> Appointments { get; set; } = [];


        // Row version for optimistic concurrency control
        public byte[] RowVersion { get; set; } = null!;

    }
}
