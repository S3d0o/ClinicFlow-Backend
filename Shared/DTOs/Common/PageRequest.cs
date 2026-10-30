namespace Shared.DTOs.Common
{
    // Paging for every /api/v2 list marked "paged" (API_CONTRACT.md §13.1 "Lists", docs/v2/07_API_DESIGN.md §3):
    // pageNumber and pageSize in the query string, defaults 1 and 20, pageSize at most 100; out of range is 400.
    // A list filter derives from it (record BranchFilterRequest : PageRequest). PageRequestValidator then applies to the
    // derived type without being included, because ValidationFilter also runs the validators of base types.
    // The service returns Result<PagedList<T>> and the controller HandlePagedResult (X-Total-Count).
    // Inherited routes keep their own defaults and limits (contract §1)
    public record PageRequest
    {
        public const int DefaultPageNumber = 1;
        public const int DefaultPageSize = 20;
        public const int MaxPageSize = 100;

        public int PageNumber { get; init; } = DefaultPageNumber;
        public int PageSize { get; init; } = DefaultPageSize;

        // Rows before this page; a method, so model binding and OpenAPI don't treat it as a query parameter
        public int Skip() => (PageNumber - 1) * PageSize;
    }
}
