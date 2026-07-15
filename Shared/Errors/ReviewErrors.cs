using Shared.ResultPattern;

namespace Shared.Errors
{
    public static class ReviewErrors
    {
        public static Error NotFound(int id) =>
            Error.NotFound("Review.NotFound", $"Review with id {id} was not found");

        public static readonly Error AlreadyExists =
            Error.Conflict("Review.AlreadyExists", "A review already exists for this appointment");

        public static readonly Error NotAllowed =
            Error.Forbidden("Review.NotAllowed", "Reviews can only be submitted for completed appointments");

        public static readonly Error Unauthorized =
            Error.Forbidden("Review.Unauthorized", "You are not authorized to delete this review");
    }
}
