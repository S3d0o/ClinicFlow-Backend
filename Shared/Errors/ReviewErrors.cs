using Shared.ResultPattern;

namespace Shared.Errors
{
    public static class ReviewErrors
    {
        public static Error NotFound(int id) =>
            Error.NotFound("Review.NotFound", $"Review with id {id} was not found");

        // 403 (not the owner). The code keeps its original "Unauthorized" name: it is part of the API contract
        public static readonly Error Forbidden =
            Error.Forbidden("Review.Unauthorized", "You are not authorized to delete this review");
    }
}
