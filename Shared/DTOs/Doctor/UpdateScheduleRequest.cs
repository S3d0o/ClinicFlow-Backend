namespace Shared.DTOs.Doctor
{
    public record UpdateScheduleRequest
    {
        public TimeOnly? StartTime { get; init; }       // e.g. 09:00
        public TimeOnly? EndTime { get; init; }         // e.g. 17:00
        public int? SlotDurationMinutes { get; init; } = 30;
        public bool? IsActive { get; init; } = true;

    }
}
