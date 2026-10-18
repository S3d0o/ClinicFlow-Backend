using Domain.Entities.AppModule;
using NSubstitute;
using Shared.DTOs.Doctor;
using Shared.Errors;

namespace ClinicFlow.Tests.Services.DoctorServiceTests
{
    public class UpdateDoctorProfileAsyncTests : DoctorServiceTestsBase
    {
        private readonly Guid _userId = Guid.NewGuid();
        private readonly CancellationToken _ct = new CancellationTokenSource().Token;
        private readonly DoctorProfile _doctor;

        public UpdateDoctorProfileAsyncTests()
        {
            _doctor = new DoctorProfile
            {
                Id = 2,
                UserId = _userId,
                SpecialtyId = 3,
                Bio = "Old bio",
                ConsultationFee = 300m,
                ClinicAddress = "Old address",
                ClinicCity = "Giza",
                YearsOfExperience = 9,
                IsApprovedByAdmin = false,
                AverageRating = 4.2,
                TotalReviews = 11
            };
            _uow.Doctors.GetByUserIdAsync(_userId, _ct).Returns(_doctor);
        }

        [Fact]
        public async Task UpdateDoctorProfileAsync_WhenAllFieldsSent_UpdatesThemAndSaves()
        {
            var result = await _sut.UpdateDoctorProfileAsync(_userId, new UpdateDoctorProfileRequest
            {
                Bio = "New bio", ConsultationFee = 450m, ClinicAddress = "12 Tahrir St", ClinicCity = "Cairo"
            }, _ct);

            Assert.True(result.IsSuccess);
            Assert.Equal("New bio", _doctor.Bio);
            Assert.Equal(450m, _doctor.ConsultationFee);
            Assert.Equal("12 Tahrir St", _doctor.ClinicAddress);
            Assert.Equal("Cairo", _doctor.ClinicCity);
            await _uow.Received(1).SaveChangesAsync(_ct);
        }

        [Fact]
        public async Task UpdateDoctorProfileAsync_WhenTextFieldsNull_KeepsTheirCurrentValues()
        {
            var result = await _sut.UpdateDoctorProfileAsync(_userId, new UpdateDoctorProfileRequest { ConsultationFee = 450m }, _ct);

            Assert.True(result.IsSuccess);
            Assert.Equal("Old bio", _doctor.Bio);
            Assert.Equal("Old address", _doctor.ClinicAddress);
            Assert.Equal("Giza", _doctor.ClinicCity);
            Assert.Equal(450m, _doctor.ConsultationFee);
        }

        [Fact]
        public async Task UpdateDoctorProfileAsync_Always_LeavesApprovalSpecialtyAndRatingUntouched()
        {
            // A doctor must not be able to approve themselves or change their rating through this endpoint
            await _sut.UpdateDoctorProfileAsync(_userId, new UpdateDoctorProfileRequest
            {
                Bio = "New bio", ConsultationFee = 450m, ClinicAddress = "12 Tahrir St", ClinicCity = "Cairo"
            }, _ct);

            Assert.False(_doctor.IsApprovedByAdmin);
            Assert.Equal(3, _doctor.SpecialtyId);
            Assert.Equal(9, _doctor.YearsOfExperience);
            Assert.Equal(4.2, _doctor.AverageRating);
            Assert.Equal(11, _doctor.TotalReviews);
            Assert.Equal(_userId, _doctor.UserId);
        }

        [Fact]
        public async Task UpdateDoctorProfileAsync_WhenFeeOmitted_KeepsTheCurrentFee()
        {
            // REVIEW.md M15: the fee is optional, and an omitted fee must not be written as 0
            var result = await _sut.UpdateDoctorProfileAsync(_userId, new UpdateDoctorProfileRequest { Bio = "Only the bio" }, _ct);

            Assert.True(result.IsSuccess);
            Assert.Equal(300m, _doctor.ConsultationFee);
            Assert.Equal("Only the bio", _doctor.Bio);
        }

        [Fact]
        public async Task UpdateDoctorProfileAsync_WhenProfileNotFound_ReturnsProfileNotFoundError()
        {
            var unknownUserId = Guid.NewGuid();
            _uow.Doctors.GetByUserIdAsync(unknownUserId, _ct).Returns((DoctorProfile?)null);

            var result = await _sut.UpdateDoctorProfileAsync(unknownUserId, new UpdateDoctorProfileRequest { ConsultationFee = 450m }, _ct);

            Assert.True(result.IsFailure);
            Assert.Contains(DoctorErrors.ProfileNotFound(unknownUserId), result.Errors);
            await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        }
    }
}
