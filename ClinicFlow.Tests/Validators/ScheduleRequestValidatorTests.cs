using FluentValidation.TestHelper;
using Presentation.Validators;
using Shared.DTOs.Doctor;

namespace ClinicFlow.Tests.Validators
{
    public class CreateScheduleRequestValidatorTests
    {
        private readonly CreateScheduleRequestValidator _validator = new();

        private static readonly CreateScheduleRequest Valid = new()
        {
            DayOfWeek = DayOfWeek.Sunday, StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(17, 0), SlotDurationMinutes = 30
        };

        [Fact]
        public void Validate_ValidRequest_HasNoErrors()
            => _validator.TestValidate(Valid).ShouldNotHaveAnyValidationErrors();

        [Fact]
        public void Validate_EndBeforeStart_FailsOnEndTime()
            => _validator.TestValidate(Valid with { StartTime = new TimeOnly(17, 0), EndTime = new TimeOnly(9, 0) })
                .ShouldHaveValidationErrorFor(x => x.EndTime);

        [Fact]
        public void Validate_MissingTimes_FailOnBothTimes()
        {
            var result = _validator.TestValidate(Valid with { StartTime = default, EndTime = default });
            result.ShouldHaveValidationErrorFor(x => x.StartTime);
            result.ShouldHaveValidationErrorFor(x => x.EndTime);
        }

        [Theory]
        [InlineData(9)]
        [InlineData(121)]
        public void Validate_SlotDurationOutside10To120_FailsOnSlotDuration(int minutes)
            => _validator.TestValidate(Valid with { SlotDurationMinutes = minutes })
                .ShouldHaveValidationErrorFor(x => x.SlotDurationMinutes);

        [Fact]
        public void Validate_WindowShorterThanOneSlot_FailsOnSlotDuration()
            => _validator.TestValidate(Valid with { StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(9, 20), SlotDurationMinutes = 30 })
                .ShouldHaveValidationErrorFor(x => x.SlotDurationMinutes)
                .WithErrorMessage("The time window must be large enough to fit at least one slot.");

        [Fact]
        public void Validate_InvalidDayOfWeek_FailsOnDayOfWeek()
            => _validator.TestValidate(Valid with { DayOfWeek = (DayOfWeek)9 }).ShouldHaveValidationErrorFor(x => x.DayOfWeek);
    }

    public class UpdateScheduleRequestValidatorTests
    {
        private readonly UpdateScheduleRequestValidator _validator = new();

        [Fact]
        public void Validate_EmptyRequest_IsAllowed()
            => _validator.TestValidate(new UpdateScheduleRequest()).ShouldNotHaveAnyValidationErrors();

        [Fact]
        public void Validate_BothTimesWithEndBeforeStart_FailsOnEndTime()
            => _validator.TestValidate(new UpdateScheduleRequest { StartTime = new TimeOnly(12, 0), EndTime = new TimeOnly(10, 0) })
                .ShouldHaveValidationErrorFor(x => x.EndTime);

        [Fact]
        public void Validate_OnlyEndTime_IsNotComparedHere()
        {
            // The merged times are checked in the service (Doctor.InvalidScheduleTime), not here
            _validator.TestValidate(new UpdateScheduleRequest { EndTime = new TimeOnly(8, 0) }).ShouldNotHaveAnyValidationErrors();
        }

        [Theory]
        [InlineData(5)]
        [InlineData(180)]
        public void Validate_SlotDurationOutside10To120_FailsOnSlotDuration(int minutes)
            => _validator.TestValidate(new UpdateScheduleRequest { SlotDurationMinutes = minutes })
                .ShouldHaveValidationErrorFor(x => x.SlotDurationMinutes);
    }
}
