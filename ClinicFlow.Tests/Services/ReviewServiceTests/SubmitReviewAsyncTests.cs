using Domain.Entities.AppModule;
using Domain.Entities.IdentityModule;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shared.DTOs.Review;
using Shared.Errors;

namespace ClinicFlow.Tests.Services.ReviewServiceTests
{
    public class SubmitReviewAsyncTests : ReviewServiceTestsBase
    {
        private const int _appointmentId = 10;
        private const int _patientId = 2;
        private const int _doctorId = 5;
        private readonly Guid _patientUserId = Guid.NewGuid();
        private readonly CancellationToken _ct = CancellationToken.None;
        private readonly SubmitReviewRequest _request = new(_appointmentId, 4, "Very thorough");
        private readonly PatientProfile _patient;
        private readonly Appointment _appointment;

        public SubmitReviewAsyncTests()
        {
            _patient = new PatientProfile
            {
                Id = _patientId,
                UserId = _patientUserId,
                User = new ApplicationUser { FirstName = "Saad", LastName = "Mohamed" }
            };
            _appointment = new Appointment
            {
                Id = _appointmentId,
                PatientProfileId = _patientId,
                DoctorProfileId = _doctorId,
                Status = AppointmentStatus.Completed
            };

            _uow.Patients.GetPatientByUserIdAsync(_patientUserId, _ct).Returns(_patient);
            _uow.Reviews.GetByAppointmentIdAsync(_appointmentId, _ct).Returns((Review?)null);
            _uow.Appointments.GetByIdAsync(_appointmentId, _ct).Returns(_appointment);
        }

        [Fact]
        public async Task SubmitReviewAsync_WhenValid_AddsReviewFromAppointment()
        {
            Review? added = null;
            await _uow.Reviews.AddAsync(Arg.Do<Review>(r => added = r), Arg.Any<CancellationToken>());

            var result = await _sut.SubmitReviewAsync(_patientUserId, _request, _ct);

            Assert.True(result.IsSuccess);
            Assert.NotNull(added);
            Assert.Equal(_appointmentId, added!.AppointmentId);
            Assert.Equal(_patientId, added.PatientProfileId);
            Assert.Equal(_doctorId, added.DoctorProfileId);
            Assert.Equal(4, added.Rating);
            Assert.Equal("Very thorough", added.Comment);
            Assert.True(added.IsVisible);
            Assert.Equal(4, result.Value.Rating);
            Assert.Equal("Very thorough", result.Value.Comment);
        }

        [Fact]
        public async Task SubmitReviewAsync_WhenValid_RefreshesRatingCacheInSameTransaction()
        {
            var result = await _sut.SubmitReviewAsync(_patientUserId, _request, _ct);

            // The insert and the rating refresh commit together (REVIEW.md L8)
            Assert.True(result.IsSuccess);
            Received.InOrder(() =>
            {
                _uow.BeginTransactionAsync(_ct);
                _uow.SaveChangesAsync(_ct);
                _uow.Doctors.RefreshRatingCacheAsync(_doctorId, _ct);
                _transaction.CommitAsync(_ct);
            });
        }

        [Fact]
        public async Task SubmitReviewAsync_WhenPatientProfileNotFound_ReturnsProfileNotFoundError()
        {
            var unknownUserId = Guid.NewGuid();
            _uow.Patients.GetPatientByUserIdAsync(unknownUserId, _ct).Returns((PatientProfile?)null);

            var result = await _sut.SubmitReviewAsync(unknownUserId, _request, _ct);

            Assert.True(result.IsFailure);
            Assert.Contains(PatientErrors.ProfileNotFound(unknownUserId), result.Errors);
            await _uow.Reviews.DidNotReceive().AddAsync(Arg.Any<Review>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task SubmitReviewAsync_WhenReviewAlreadyExists_ReturnsReviewAlreadyExistsError()
        {
            _uow.Reviews.GetByAppointmentIdAsync(_appointmentId, _ct).Returns(new Review { AppointmentId = _appointmentId });

            var result = await _sut.SubmitReviewAsync(_patientUserId, _request, _ct);

            Assert.True(result.IsFailure);
            Assert.Contains(AppointmentErrors.ReviewAlreadyExists, result.Errors);
            await _uow.Reviews.DidNotReceive().AddAsync(Arg.Any<Review>(), Arg.Any<CancellationToken>());
            await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task SubmitReviewAsync_WhenAppointmentNotFound_ReturnsNotFoundError()
        {
            _uow.Appointments.GetByIdAsync(_appointmentId, _ct).Returns((Appointment?)null);

            var result = await _sut.SubmitReviewAsync(_patientUserId, _request, _ct);

            Assert.True(result.IsFailure);
            Assert.Contains(AppointmentErrors.NotFound(_appointmentId), result.Errors);
            await _uow.Reviews.DidNotReceive().AddAsync(Arg.Any<Review>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task SubmitReviewAsync_WhenAppointmentBelongsToAnotherPatient_ReturnsUnauthorizedError()
        {
            _appointment.PatientProfileId = _patientId + 1;

            var result = await _sut.SubmitReviewAsync(_patientUserId, _request, _ct);

            Assert.True(result.IsFailure);
            Assert.Contains(AppointmentErrors.Unauthorized, result.Errors);
            await _uow.Reviews.DidNotReceive().AddAsync(Arg.Any<Review>(), Arg.Any<CancellationToken>());
            await _uow.Doctors.DidNotReceive().RefreshRatingCacheAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task SubmitReviewAsync_WhenAnotherPatientsAppointmentAlreadyReviewed_ReturnsUnauthorizedError()
        {
            // Ownership is checked before the duplicate check, so the response doesn't reveal
            // whether another patient's appointment was reviewed (REVIEW.md NL22)
            _appointment.PatientProfileId = _patientId + 1;
            _uow.Reviews.GetByAppointmentIdAsync(_appointmentId, _ct).Returns(new Review { AppointmentId = _appointmentId });

            var result = await _sut.SubmitReviewAsync(_patientUserId, _request, _ct);

            Assert.True(result.IsFailure);
            Assert.Contains(AppointmentErrors.Unauthorized, result.Errors);
            Assert.DoesNotContain(AppointmentErrors.ReviewAlreadyExists, result.Errors);
            await _uow.Reviews.DidNotReceive().GetByAppointmentIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
        }

        [Theory]
        [InlineData(AppointmentStatus.Pending)]
        [InlineData(AppointmentStatus.Confirmed)]
        [InlineData(AppointmentStatus.Cancelled)]
        [InlineData(AppointmentStatus.NoShow)]
        public async Task SubmitReviewAsync_WhenAppointmentNotCompleted_ReturnsReviewNotAllowedError(AppointmentStatus status)
        {
            _appointment.Status = status;

            var result = await _sut.SubmitReviewAsync(_patientUserId, _request, _ct);

            Assert.True(result.IsFailure);
            Assert.Contains(AppointmentErrors.ReviewNotAllowed, result.Errors);
            await _uow.Reviews.DidNotReceive().AddAsync(Arg.Any<Review>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task SubmitReviewAsync_WhenConcurrentRequestInsertedReviewFirst_ReturnsReviewAlreadyExistsError()
        {
            // The pre-check sees no review; the insert then hits the unique index because another request won the race
            _uow.Reviews.GetByAppointmentIdAsync(_appointmentId, _ct)
                .Returns((Review?)null, new Review { AppointmentId = _appointmentId });
            _uow.SaveChangesAsync(_ct).ThrowsAsync(new DbUpdateException("unique index IX_Reviews_AppointmentId"));

            var result = await _sut.SubmitReviewAsync(_patientUserId, _request, _ct);

            Assert.True(result.IsFailure);
            Assert.Contains(AppointmentErrors.ReviewAlreadyExists, result.Errors);
            await _uow.Doctors.DidNotReceive().RefreshRatingCacheAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
            await _transaction.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task SubmitReviewAsync_WhenSaveFailsForAnotherReason_Throws()
        {
            // No review exists after the failure, so it was not a duplicate: the exception must propagate
            _uow.SaveChangesAsync(_ct).ThrowsAsync(new DbUpdateException("check constraint CK_Review_Rating"));

            await Assert.ThrowsAsync<DbUpdateException>(() => _sut.SubmitReviewAsync(_patientUserId, _request, _ct));
            await _uow.Doctors.DidNotReceive().RefreshRatingCacheAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
        }
    }
}
