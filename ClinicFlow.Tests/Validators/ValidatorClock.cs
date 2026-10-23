using NSubstitute;
using Services.Abstraction.Contracts;

namespace ClinicFlow.Tests.Validators
{
    // Date-of-birth rules compare with the clinic's date, so the validators get a clock frozen on a known day
    internal static class ValidatorClock
    {
        public static readonly DateOnly ClinicToday = new(2026, 10, 23);

        public static IClock Create()
        {
            var clock = Substitute.For<IClock>();
            clock.ClinicToday.Returns(ClinicToday);
            return clock;
        }
    }
}
