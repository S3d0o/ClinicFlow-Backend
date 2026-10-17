using Domain.Enums;

namespace Shared.DTOs.Appointment
{
    public record AppointmentFilterRequest
    {
        public AppointmentStatus? Status { get; init; }
        public int PageNumber { get; init; } = 1;
        public int PageSize { get; init; } = 10;
    }
}
