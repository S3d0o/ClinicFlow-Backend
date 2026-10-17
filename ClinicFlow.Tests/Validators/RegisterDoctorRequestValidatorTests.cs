using FluentValidation.TestHelper;
using Presentation.Validators;
using Shared.DTOs.Auth;
using Shared.Enums;

namespace ClinicFlow.Tests.Validators
{
    public class RegisterDoctorRequestValidatorTests
    {
        private readonly RegisterDoctorRequestValidator _validator = new();

        private static readonly RegisterDoctorRequest Valid = new(
            "Omar", "Fathy", "omar@test.com", "Doctor@1234", "+201009876543", Gender.Male,
            new DateOnly(1980, 7, 2), 3, "Cardiologist", 12, 450m, "12 Tahrir St", "Cairo");

        [Fact]
        public void Validate_ValidRequest_HasNoErrors()
            => _validator.TestValidate(Valid).ShouldNotHaveAnyValidationErrors();

        [Theory]
        [InlineData("doctor@1234")]
        [InlineData("Doctor1234")]
        [InlineData("Dr@1")]
        public void Validate_WeakPassword_FailsOnPassword(string password)
            => _validator.TestValidate(Valid with { Password = password }).ShouldHaveValidationErrorFor(x => x.Password);

        [Fact]
        public void Validate_PhoneNotE164_FailsOnPhoneNumber()
            => _validator.TestValidate(Valid with { PhoneNumber = "01009876543" }).ShouldHaveValidationErrorFor(x => x.PhoneNumber);

        [Fact]
        public void Validate_UnderEighteen_FailsOnDateOfBirth()
            => _validator.TestValidate(Valid with { DateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-10)) })
                .ShouldHaveValidationErrorFor(x => x.DateOfBirth);

        [Fact]
        public void Validate_NegativeFeeAndExperience_AreNotCheckedYet()
        {
            // Documents REVIEW.md M13 and contract #2: these fields aren't validated, so bad values
            // only fail later (an unknown SpecialtyId fails on the foreign key as RegistrationFailed)
            _validator.TestValidate(Valid with { ConsultationFee = -50m, YearsOfExperience = -3, SpecialtyId = 0 })
                .ShouldNotHaveAnyValidationErrors();
        }
    }
}
