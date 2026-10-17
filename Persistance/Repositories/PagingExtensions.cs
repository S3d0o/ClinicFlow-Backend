namespace Persistence.Repositories
{
    internal static class PagingExtensions
    {
        // Validators reject bad paging at the API; clamping here keeps a direct caller from
        // reaching Skip(-n) (an exception) or Take(0) (an empty page) (REVIEW.md H6)
        public static IQueryable<T> Page<T>(this IQueryable<T> query, int pageNumber, int pageSize)
        {
            pageNumber = Math.Max(1, pageNumber);
            pageSize = Math.Max(1, pageSize);
            return query.Skip((pageNumber - 1) * pageSize).Take(pageSize);
        }
    }
}
