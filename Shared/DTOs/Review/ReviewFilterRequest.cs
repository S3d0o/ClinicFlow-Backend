namespace Shared.DTOs.Review
{
    public record ReviewFilterRequest
    {
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 10;
    }
}
