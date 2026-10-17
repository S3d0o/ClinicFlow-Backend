using FluentValidation.TestHelper;
using Presentation.Validators;
using Shared.DTOs.Auth;
using Shared.Enums;

namespace ClinicFlow.Tests.Validators
{
    public class RegisterPatientRequestValidatorTests
    {
        private readonly RegisterPatientRequestValidator _validator = new();

        private static readonly RegisterPatientRequest Valid = new(
            "Mona", "Adel", "mona@test.com", "Patient@1234", "+201001234567", Gender.Female,
            new DateOnly(1995, 3, 14), null, null, null, null, "+201112223334");

        [Fact]
        public void Validate_ValidRequest_HasNoErrors()
            => _validator.TestValidate(Valid).ShouldNotHaveAnyValidationErrors();

        [Theory]
        [InlineData("short1!")]       // < 8 characters
        [InlineData("patient@1234")]  // no upper case
        [InlineData("PATIENT@1234")]  // no lower case
        [InlineData("Patient@abcd")]  // no digit
        [InlineData("Patient12345")]  // no special character
        [InlineData("")]
        public void Validate_WeakPassword_FailsOnPassword(string password)
            => _validator.TestValidate(Valid with { Password = password }).ShouldHaveValidationErrorFor(x => x.Password);

        [Theory]
        [InlineData("01001234567")]   // leading 0 isn't E.164
        [InlineData("+20 100 123")]   // spaces
        [InlineData("phone")]
        [InlineData("")]
        public void Validate_PhoneNotE164_FailsOnPhoneNumber(string phone)
            => _validator.TestValidate(Valid with { PhoneNumber = phone }).ShouldHaveValidationErrorFor(x => x.PhoneNumber);

        [Theory]
        [InlineData("not-an-email")]
        [InlineData("")]
        public void Validate_BadEmail_FailsOnEmail(string email)
            => _validator.TestValidate(Valid with { Email = email }).ShouldHaveValidationErrorFor(x => x.Email);

        [Fact]
        public void Validate_UnderEighteen_FailsOnDateOfBirth()
        {
            var seventeen = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-17));
            _validator.TestValidate(Valid with { DateOfBirth = seventeen }).ShouldHaveValidationErrorFor(x => x.DateOfBirth);
        }

        [Fact]
        public void Validate_NoDateOfBirth_IsAllowed()
            => _validator.TestValidate(Valid with { DateOfBirth = null }).ShouldNotHaveValidationErrorFor(x => x.DateOfBirth);

        [Fact]
        public void Validate_NameOver50Characters_FailsOnNames()
        {
            var result = _validator.TestValidate(Valid with { FirstName = new string('a', 51), LastName = "" });
            result.ShouldHaveValidationErrorFor(x => x.FirstName);
            result.ShouldHaveValidationErrorFor(x => x.LastName);
        }

        [Fact]
        public void Validate_EmergencyPhoneNotE164_FailsOnEmergencyContactPhone()
            => _validator.TestValidate(Valid with { EmergencyContactPhone = "0111222333" })
                .ShouldHaveValidationErrorFor(x => x.EmergencyContactPhone);
    }
}
