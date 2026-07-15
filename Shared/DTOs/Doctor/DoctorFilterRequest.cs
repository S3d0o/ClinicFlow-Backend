using Domain.Enums;

namespace Shared.DTOs.Doctor
{
    public record DoctorFilterRequest
    {
        public string Name { get; init; } = string.Empty;
        public int? SpecialtyId { get; init; }
        public string? City { get; init; }
        public int PageNumber { get; init; } = 1;
        public int PageSize { get; init; } = 10;
        public DoctorSortBy SortBy { get; init; } // "rating", "fee", "experience"
    }
}
