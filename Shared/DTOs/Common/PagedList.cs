namespace Shared.DTOs.Common
{
    // One page of a list plus the total number of matching items. Controllers return Items as the
    // body and TotalCount in the X-Total-Count header, so list responses stay plain arrays (REVIEW.md NL2)
    public record PagedList<T>(IReadOnlyList<T> Items, int TotalCount);
}
