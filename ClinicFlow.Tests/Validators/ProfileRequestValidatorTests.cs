using ClinicFlow.Domain.Enums;
using FluentValidation.TestHelper;
using Presentation.Validators;
using Shared.DTOs.Doctor;
using Shared.DTOs.Patient;
using Shared.DTOs.Profile;
using Shared.Enums;

namespace ClinicFlow.Tests.Validators
{
    public class UpdateDoctorProfileRequestValidatorTests
    {
        private readonly UpdateDoctorProfileRequestValidator _validator = new();

        [Fact]
        public void Validate_FeeOnly_IsAllowed()
            => _validator.TestValidate(new UpdateDoctorProfileRequest { ConsultationFee = 450m }).ShouldNotHaveAnyValidationErrors();

        [Fact]
        public void Validate_FeeOmitted_IsAllowed()
            // REVIEW.md M15: a partial update doesn't have to resend the fee
            => _validator.TestValidate(new UpdateDoctorProfileRequest { Bio = "Only the bio" }).ShouldNotHaveAnyValidationErrors();

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(10000.01)]
        public void Validate_FeeOutside0To10000_FailsOnFee(decimal fee)
            => _validator.TestValidate(new UpdateDoctorProfileRequest { ConsultationFee = fee })
                .ShouldHaveValidationErrorFor(x => x.ConsultationFee);

        [Fact]
        public void Validate_TextOverLimits_FailsOnEachField()
        {
            var result = _validator.TestValidate(new UpdateDoctorProfileRequest
            {
                ConsultationFee = 450m, Bio = new string('a', 501), ClinicAddress = new string('a', 301), ClinicCity = new string('a', 101)
            });
            result.ShouldHaveValidationErrorFor(x => x.Bio);
            result.ShouldHaveValidationErrorFor(x => x.ClinicAddress);
            result.ShouldHaveValidationErrorFor(x => x.ClinicCity);
        }
    }

    public class UpdatePatientProfileRequestValidatorTests
    {
        private readonly UpdatePatientProfileRequestValidator _validator = new();

        [Fact]
        public void Validate_EmptyRequest_IsAllowed()
            => _validator.TestValidate(new UpdatePatientProfileRequest()).ShouldNotHaveAnyValidationErrors();

        [Fact]
        public void Validate_InvalidBloodType_FailsOnBloodType()
            => _validator.TestValidate(new UpdatePatientProfileRequest { BloodType = (BloodType)99 })
                .ShouldHaveValidationErrorFor(x => x.BloodType);

        [Theory]
        [InlineData("12345")]        // too short
        [InlineData("+20-100-1234")] // dashes
        public void Validate_BadEmergencyPhone_FailsOnEmergencyContactPhone(string phone)
            => _validator.TestValidate(new UpdatePatientProfileRequest { EmergencyContactPhone = phone })
                .ShouldHaveValidationErrorFor(x => x.EmergencyContactPhone);

        [Fact]
        public void Validate_LocalPhoneWithLeadingZero_IsAllowedHereButNotAtRegistration()
        {
            // Documents REVIEW.md M13: updates accept ^\+?[0-9]{7,15}$, registration requires E.164
            _validator.TestValidate(new UpdatePatientProfileRequest { EmergencyContactPhone = "01001234567" })
                .ShouldNotHaveAnyValidationErrors();
        }

        [Fact]
        public void Validate_TextOverLimits_FailsOnEachField()
        {
            var result = _validator.TestValidate(new UpdatePatientProfileRequest
            {
                Allergies = new string('a', 501), ChronicConditions = new string('a', 501), EmergencyContactName = new string('a', 101)
            });
            result.ShouldHaveValidationErrorFor(x => x.Allergies);
            result.ShouldHaveValidationErrorFor(x => x.ChronicConditions);
            result.ShouldHaveValidationErrorFor(x => x.EmergencyContactName);
        }
    }

    public class UpdateUserProfileRequestValidatorTests
    {
        private readonly UpdateUserProfileRequestValidator _validator = new();

        [Fact]
        public void Validate_EmptyRequest_IsAllowed()
            => _validator.TestValidate(new UpdateUserProfileRequest()).ShouldNotHaveAnyValidationErrors();

        [Fact]
        public void Validate_RelativePictureUrl_FailsOnProfilePictureUrl()
            => _validator.TestValidate(new UpdateUserProfileRequest { ProfilePictureUrl = "/images/me.png" })
                .ShouldHaveValidationErrorFor(x => x.ProfilePictureUrl);

        [Fact]
        public void Validate_DateOfBirthTodayOrLater_FailsOnDateOfBirth()
            => _validator.TestValidate(new UpdateUserProfileRequest { DateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow) })
                .ShouldHaveValidationErrorFor(x => x.DateOfBirth);

        [Fact]
        public void Validate_InvalidGenderAndLongName_FailOnThoseFields()
        {
            var result = _validator.TestValidate(new UpdateUserProfileRequest { Gender = (Gender)7, FirstName = new string('a', 51) });
            result.ShouldHaveValidationErrorFor(x => x.Gender);
            result.ShouldHaveValidationErrorFor(x => x.FirstName);
        }
    }
}
