using Domain.Enums;

namespace Shared.DTOs.Appointment
{
    public record AppointmentFilterRequest
    {
        public AppointmentStatus? Status { get; init; }
        public int PageNumber { get; init; }
        public int PageSize { get; init; }
    }
}
