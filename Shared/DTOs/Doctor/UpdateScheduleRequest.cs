namespace Shared.DTOs.Doctor
{
    public record UpdateScheduleRequest
    {
        public TimeOnly? StartTime { get; init; }       // e.g. 09:00
        public TimeOnly? EndTime { get; init; }         // e.g. 17:00
        // No defaults: a field that is not sent keeps its current value
        public int? SlotDurationMinutes { get; init; }
        public bool? IsActive { get; init; }

    }
}
