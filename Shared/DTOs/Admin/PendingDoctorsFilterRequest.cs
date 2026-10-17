namespace Shared.DTOs.Admin
{
    public record PendingDoctorsFilterRequest
    {
        public int PageNumber { get; init; } = 1;
        // Omitted: every pending doctor, as before; sent: one page of that size (REVIEW.md NL2)
        public int? PageSize { get; init; }
    }
}
