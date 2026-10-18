using ClinicFlow.Domain.Enums;
using Domain.Entities.AppModule;
using Domain.Entities.IdentityModule;
using NSubstitute;
using Shared.DTOs.Patient;
using Shared.Errors;

namespace ClinicFlow.Tests.Services.PatientServiceTests
{
    public class GetProfileAsyncTests : PatientServiceTestsBase
    {
        private readonly Guid _userId = Guid.NewGuid();
        private readonly CancellationToken _ct = new CancellationTokenSource().Token;

        [Fact]
        public async Task GetProfileAsync_WhenFound_MapsAccountAndMedicalFields()
        {
            _uow.Patients.GetPatientByUserIdForReadAsync(_userId, _ct).Returns(new PatientProfile
            {
                Id = 2,
                UserId = _userId,
                User = new ApplicationUser { FirstName = "Mona", LastName = "Adel", Email = "mona@test.com", PhoneNumber = "+201001234567" },
                BloodType = BloodType.APositive,
                Allergies = "Penicillin",
                ChronicConditions = null,
                EmergencyContactName = "Adel Ali",
                EmergencyContactPhone = "+201112223334"
            });

            var result = await _sut.GetProfileAsync(_userId, _ct);

            Assert.True(result.IsSuccess);
            Assert.Equal(new PatientProfileResponse
            {
                Id = 2, FullName = "Mona Adel", Email = "mona@test.com", PhoneNumber = "+201001234567",
                BloodType = BloodType.APositive, Allergies = "Penicillin", ChronicConditions = null,
                EmergencyContactName = "Adel Ali", EmergencyContactPhone = "+201112223334"
            }, result.Value);
        }

        [Fact]
        public async Task GetProfileAsync_WhenNotFound_ReturnsProfileNotFoundError()
        {
            var result = await _sut.GetProfileAsync(_userId, _ct);

            Assert.True(result.IsFailure);
            Assert.Contains(PatientErrors.ProfileNotFound(_userId), result.Errors);
        }
    }
}
