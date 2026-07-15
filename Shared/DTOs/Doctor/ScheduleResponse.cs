using System.Reflection.Metadata.Ecma335;

namespace Shared.DTOs.Doctor
{
    public record ScheduleResponse
    {
        public int Id { get; init; }
        public DayOfWeek DayOfWeek { get; init; }     // 0 = Sunday … 6 = Saturday
        public TimeOnly StartTime { get; init; }       // e.g. 09:00
        public TimeOnly EndTime { get; init; }         // e.g. 17:00
        public int SlotDurationMinutes { get; init; } = 30;
        public bool IsActive { get; init; } = true;
    }
}
