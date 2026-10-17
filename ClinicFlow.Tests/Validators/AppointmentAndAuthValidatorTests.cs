using Domain.Enums;
using FluentValidation.TestHelper;
using Presentation.Validators;
using Shared.DTOs.Appointment;
using Shared.DTOs.Auth;
using Shared.Errors;

namespace ClinicFlow.Tests.Validators
{
    // REVIEW.md H6 and M13: requests that had no validator
    public class AppointmentFilterRequestValidatorTests
    {
        private readonly AppointmentFilterRequestValidator _validator = new();

        [Fact]
        public void Validate_Defaults_AreFirstPageOfTenAndValid()
        {
            var request = new AppointmentFilterRequest();

            Assert.Equal(1, request.PageNumber);
            Assert.Equal(10, request.PageSize);
            _validator.TestValidate(request).ShouldNotHaveAnyValidationErrors();
        }

        [Theory]
        [InlineData(0, 10)]
        [InlineData(-1, 10)]
        [InlineData(1, 0)]
        [InlineData(1, 51)]
        public void Validate_PagingOutOfRange_Fails(int pageNumber, int pageSize)
            => Assert.False(_validator.TestValidate(new AppointmentFilterRequest { PageNumber = pageNumber, PageSize = pageSize }).IsValid);

        [Fact]
        public void Validate_UnknownStatus_FailsOnStatus()
            => _validator.TestValidate(new AppointmentFilterRequest { Status = (AppointmentStatus)42 })
                .ShouldHaveValidationErrorFor(x => x.Status);
    }

    public class AppointmentRequestValidatorTests
    {
        [Fact]
        public void Book_ValidRequest_HasNoErrors()
            => new BookAppointmentRequestValidator().TestValidate(new BookAppointmentRequest(1, "Checkup"))
                .ShouldNotHaveAnyValidationErrors();

        [Fact]
        public void Book_BadSlotAndLongReason_FailOnThoseFields()
        {
            var result = new BookAppointmentRequestValidator().TestValidate(new BookAppointmentRequest(0, new string('a', 501)));

            result.ShouldHaveValidationErrorFor(x => x.SlotId);
            result.ShouldHaveValidationErrorFor(x => x.ReasonForVisit);
        }

        [Fact]
        public void Cancel_ReasonOver500_FailsAndNullIsAllowed()
        {
            var validator = new CancelAppointmentRequestValidator();

            validator.TestValidate(new CancelAppointmentRequest(new string('a', 501))).ShouldHaveValidationErrorFor(x => x.CancellationReason);
            validator.TestValidate(new CancelAppointmentRequest(null)).ShouldNotHaveAnyValidationErrors();
        }

        [Fact]
        public void Notes_Over4000_FailsAndNullIsAllowed()
        {
            var validator = new DoctorNotesRequestValidator();

            validator.TestValidate(new DoctorNotesRequest(new string('a', 4001))).ShouldHaveValidationErrorFor(x => x.notes);
            validator.TestValidate(new DoctorNotesRequest(null)).ShouldNotHaveAnyValidationErrors();
        }
    }

    public class AuthRequestValidatorTests
    {
        [Fact]
        public void Login_ValidRequest_HasNoErrors()
            => new LoginRequestValidator().TestValidate(new LoginRequest("patient@test.com", "anything"))
                .ShouldNotHaveAnyValidationErrors();

        [Fact]
        public void Login_EmptyOrMalformed_FailsOnEachField()
        {
            var result = new LoginRequestValidator().TestValidate(new LoginRequest("not-an-email", ""));

            result.ShouldHaveValidationErrorFor(x => x.Email);
            result.ShouldHaveValidationErrorFor(x => x.Password);
        }

        [Theory]
        [InlineData("password1!")]  // no uppercase
        [InlineData("Password1")]   // no symbol
        [InlineData("Pa1!")]        // too short
        public void ResetPassword_WeakPassword_FailsLikeRegistration(string password)
            => new ResetPasswordRequestValidator().TestValidate(new ResetPasswordRequest("user-id", "token", password))
                .ShouldHaveValidationErrorFor(x => x.NewPassword);

        [Fact]
        public void ResetPassword_MissingUserAndToken_FailOnThoseFields()
        {
            var result = new ResetPasswordRequestValidator().TestValidate(new ResetPasswordRequest("", "", "Strong@1234"));

            result.ShouldHaveValidationErrorFor(x => x.UserId);
            result.ShouldHaveValidationErrorFor(x => x.Token);
            result.ShouldNotHaveValidationErrorFor(x => x.NewPassword);
        }
    }
}
