using ClinicFlow.Domain.Enums;

namespace Shared.DTOs.Doctor
{
    public record SlotResponse
    {
        public int Id { get; init; }
        public DateOnly Date { get; init; }
        public TimeOnly StartTime { get; init; }
        public TimeOnly EndTime { get; init; }
        public SlotStatus Status { get; init; } = SlotStatus.Available;
    }
}
