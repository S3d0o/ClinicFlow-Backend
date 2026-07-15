namespace Domain.Parameters
{
    public class DoctorFilterParams
    {
        public string Name { get; set; } = string.Empty;
        public int? SpecialtyId { get; set; }
        public string? City { get; set; }
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 10;
        public DoctorSortBy SortBy { get; set; } // "rating", "fee", "experience"
    }
}
