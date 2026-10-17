using ClinicFlow.Domain.Enums;
using Domain.Entities.AppModule;
using NSubstitute;
using Shared.DTOs.Patient;
using Shared.Errors;

namespace ClinicFlow.Tests.Services.PatientServiceTests
{
    public class UpdateProfileAsyncTests : PatientServiceTestsBase
    {
        private readonly Guid _userId = Guid.NewGuid();
        private readonly CancellationToken _ct = new CancellationTokenSource().Token;
        private readonly PatientProfile _patient;

        public UpdateProfileAsyncTests()
        {
            _patient = new PatientProfile
            {
                Id = 2,
                UserId = _userId,
                BloodType = BloodType.OPositive,
                Allergies = "Penicillin",
                ChronicConditions = "Asthma",
                EmergencyContactName = "Adel Ali",
                EmergencyContactPhone = "+201112223334"
            };
            _uow.Patients.GetPatientByUserIdAsync(_userId, _ct).Returns(_patient);
        }

        [Fact]
        public async Task UpdateProfileAsync_WhenAllFieldsSent_UpdatesThemAndSaves()
        {
            var result = await _sut.UpdateProfileAsync(_userId, new UpdatePatientProfileRequest
            {
                BloodType = BloodType.ABNegative, Allergies = "None", ChronicConditions = "Diabetes",
                EmergencyContactName = "Sara Adel", EmergencyContactPhone = "+201009998887"
            }, _ct);

            Assert.True(result.IsSuccess);
            Assert.Equal(BloodType.ABNegative, _patient.BloodType);
            Assert.Equal("None", _patient.Allergies);
            Assert.Equal("Diabetes", _patient.ChronicConditions);
            Assert.Equal("Sara Adel", _patient.EmergencyContactName);
            Assert.Equal("+201009998887", _patient.EmergencyContactPhone);
            await _uow.Received(1).SaveChangesAsync(_ct);
        }

        [Fact]
        public async Task UpdateProfileAsync_WhenFieldsNull_KeepsTheirCurrentValues()
        {
            // Includes BloodType, a nullable value type: the ForAllMembers null check that fails for
            // TimeOnly? -> TimeOnly (REVIEW.md H5) does work for BloodType? -> BloodType?
            var result = await _sut.UpdateProfileAsync(_userId, new UpdatePatientProfileRequest { Allergies = "Latex" }, _ct);

            Assert.True(result.IsSuccess);
            Assert.Equal("Latex", _patient.Allergies);
            Assert.Equal(BloodType.OPositive, _patient.BloodType);
            Assert.Equal("Asthma", _patient.ChronicConditions);
            Assert.Equal("Adel Ali", _patient.EmergencyContactName);
            Assert.Equal("+201112223334", _patient.EmergencyContactPhone);
        }

        [Fact]
        public async Task UpdateProfileAsync_Always_LeavesIdentityFieldsUntouched()
        {
            await _sut.UpdateProfileAsync(_userId, new UpdatePatientProfileRequest { Allergies = "Latex" }, _ct);

            Assert.Equal(2, _patient.Id);
            Assert.Equal(_userId, _patient.UserId);
        }

        [Fact]
        public async Task UpdateProfileAsync_WhenProfileNotFound_ReturnsProfileNotFoundError()
        {
            var unknownUserId = Guid.NewGuid();

            var result = await _sut.UpdateProfileAsync(unknownUserId, new UpdatePatientProfileRequest { Allergies = "Latex" }, _ct);

            Assert.True(result.IsFailure);
            Assert.Contains(PatientErrors.ProfileNotFound(unknownUserId), result.Errors);
            await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        }
    }
}
