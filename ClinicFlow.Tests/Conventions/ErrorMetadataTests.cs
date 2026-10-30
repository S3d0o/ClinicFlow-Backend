using Domain.ResultPattern;

namespace ClinicFlow.Tests.Conventions
{
    // Error metadata, rendered as ProblemDetails extensions (API_CONTRACT.md §13.1 "Error extensions", P1 PR 0)
    public class ErrorMetadataTests
    {
        private static readonly Error Base = Error.Validation("Otp.InvalidCode", "Wrong code");

        [Fact]
        public void NewError_HasNoMetadata()
            => Assert.Empty(Base.Metadata);

        [Fact]
        public void WithMetadata_ReturnsACopy_AndLeavesTheOriginalUnchanged()
        {
            var withMetadata = Base.WithMetadata("hint", "x");

            Assert.Empty(Base.Metadata);
            Assert.Equal("x", withMetadata.Metadata["hint"]);
            Assert.Equal(Base.Code, withMetadata.Code);
            Assert.Equal(Base.Description, withMetadata.Description);
            Assert.Equal(Base.Type, withMetadata.Type);
        }

        [Fact]
        public void WithMetadata_Chained_KeepsEveryMember()
        {
            var error = Base.WithAttemptsLeft(2).WithMetadata("hint", "x");

            Assert.Equal(2, error.Metadata[ErrorMetadata.AttemptsLeft]);
            Assert.Equal("x", error.Metadata["hint"]);
        }

        [Fact]
        public void WithMetadata_SameKeyTwice_KeepsTheLastValue()
            => Assert.Equal(1, Base.WithAttemptsLeft(3).WithAttemptsLeft(1).Metadata[ErrorMetadata.AttemptsLeft]);

        [Theory]
        [InlineData("type")]
        [InlineData("title")]
        [InlineData("status")]
        [InlineData("detail")]
        [InlineData("instance")]
        [InlineData("traceId")]
        [InlineData("errors")]
        [InlineData("Title")]
        public void WithMetadata_ProblemDetailsMember_Throws(string key)
            => Assert.Throws<ArgumentException>(() => Base.WithMetadata(key, "x"));

        [Theory]
        [InlineData("")]
        [InlineData(" ")]
        public void WithMetadata_BlankKey_Throws(string key)
            => Assert.Throws<ArgumentException>(() => Base.WithMetadata(key, "x"));

        [Fact]
        public void WithMetadata_NullValue_Throws()
            => Assert.Throws<ArgumentNullException>(() => Base.WithMetadata("hint", null!));

        [Fact]
        public void WithRetryAt_UtcInstant_IsStoredUnderRetryAt()
        {
            var at = new DateTime(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);

            var error = Error.TooManyRequests("Otp.ResendTooSoon", "Wait").WithRetryAt(at);

            Assert.Equal(at, error.Metadata[ErrorMetadata.RetryAt]);
        }

        [Theory]
        [InlineData(DateTimeKind.Local)]
        [InlineData(DateTimeKind.Unspecified)]
        public void WithRetryAt_NotUtc_Throws(DateTimeKind kind)
            => Assert.Throws<ArgumentException>(() => Base.WithRetryAt(new DateTime(2026, 10, 30, 12, 0, 0, kind)));

        [Fact]
        public void WithAttemptsLeft_Negative_Throws()
            => Assert.Throws<ArgumentOutOfRangeException>(() => Base.WithAttemptsLeft(-1));

        [Fact]
        public void WithAttemptsLeft_Zero_IsAllowed()
            => Assert.Equal(0, Base.WithAttemptsLeft(0).Metadata[ErrorMetadata.AttemptsLeft]);

        [Fact]
        public void Equality_SameCodeAndMetadata_AreEqual()
        {
            var a = Base.WithAttemptsLeft(2);
            var b = Error.Validation("Otp.InvalidCode", "Wrong code").WithAttemptsLeft(2);

            Assert.Equal(a, b);
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
        }

        [Fact]
        public void Equality_DifferentMetadata_AreNotEqual()
        {
            Assert.NotEqual(Base.WithAttemptsLeft(2), Base.WithAttemptsLeft(1));
            Assert.NotEqual(Base, Base.WithAttemptsLeft(1));
        }

        [Fact]
        public void TooManyRequests_HasItsOwnType()
        {
            var error = Error.TooManyRequests();

            Assert.Equal(ErrorType.TooManyRequests, error.Type);
            Assert.Equal("Request.TooMany", error.Code);
        }
    }
}
