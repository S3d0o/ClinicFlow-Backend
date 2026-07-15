namespace Shared.ResultPattern
{
    public interface IResult
    {
        bool IsSuccess { get; }
        bool IsFailure { get; }
        IReadOnlyList<Error> Errors { get; }
    }

    public class Result : IResult
    {
        // private — Result<T> no longer inherits, so no leak
        private readonly List<Error> _errors = new();

        public bool IsSuccess => _errors.Count == 0;
        public bool IsFailure => !IsSuccess;
        public IReadOnlyList<Error> Errors => _errors;

        protected Result() { }

        protected Result(Error error)
        {
            if (error is null)
                throw new ArgumentNullException(nameof(error));

            _errors.Add(error);
        }

        protected Result(IEnumerable<Error> errors)
        {
            var list = errors?.ToList()
                ?? throw new ArgumentNullException(nameof(errors));

            if (list.Count == 0)
                throw new ArgumentException(
                    "Errors collection cannot be empty.", nameof(errors));

            // Guard: Validation errors must not be mixed with domain errors.
            // A result is either a validation failure OR a domain error — never both.
            var hasValidation = list.Any(e => e.Type == ErrorType.Validation);
            var hasNonValidation = list.Any(e => e.Type != ErrorType.Validation);

            if (hasValidation && hasNonValidation)
                throw new InvalidOperationException(
                    "Cannot mix Validation errors with domain errors in a single Result.");

            _errors.AddRange(list);
        }

        public static Result Ok()
            => new();

        public static Result Fail(Error error)
            => new(error);

        public static Result Fail(IEnumerable<Error> errors)
            => new(errors);

        public static implicit operator Result(Error error)
            => Fail(error);

        public static implicit operator Result(List<Error> errors)
            => Fail(errors);
    }

    // No longer inherits Result — clean separation, no static new hiding
    public class Result<T> : IResult
    {
        private readonly List<Error> _errors = new();
        private readonly T _value;

        public bool IsSuccess => _errors.Count == 0;
        public bool IsFailure => !IsSuccess;
        public IReadOnlyList<Error> Errors => _errors;

        public T Value => IsSuccess
            ? _value
            : throw new InvalidOperationException(
                "Cannot access Value on a failed Result.");

        private Result(T value)
        {
            _value = value;
        }

        private Result(Error error)
        {
            if (error is null)
                throw new ArgumentNullException(nameof(error));

            _errors.Add(error);
            _value = default!;
        }

        private Result(IEnumerable<Error> errors)
        {
            // Guard: Errors collection must not be null or empty
            var list = errors?.ToList()
                ?? throw new ArgumentNullException(nameof(errors));

            if (list.Count == 0)
                throw new ArgumentException(
                    "Errors collection cannot be empty.", nameof(errors));

            var hasValidation = list.Any(e => e.Type == ErrorType.Validation);
            var hasNonValidation = list.Any(e => e.Type != ErrorType.Validation);

            if (hasValidation && hasNonValidation)
                throw new InvalidOperationException(
                    "Cannot mix Validation errors with domain errors in a single Result.");

            _errors.AddRange(list);
            _value = default!;
        }

        public static Result<T> Ok(T value)
            => new(value);

        public static Result<T> Fail(Error error)
            => new(error);

        public static Result<T> Fail(IEnumerable<Error> errors)
            => new(errors);

        public static implicit operator Result<T>(T value) => Ok(value);
        public static implicit operator Result<T>(Error error) => Fail(error);
        public static implicit operator Result<T>(List<Error> errors) => Fail(errors);
    }
}
