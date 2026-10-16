using Domain.Entities.AppModule;
using Domain.Entities.IdentityModule;
using NSubstitute;

namespace ClinicFlow.Tests.Services.AdminServiceTests
{
    public class GetPendingDoctorsAsyncTests : AdminServiceTestsBase
    {
        private readonly CancellationToken _ct = CancellationToken.None;

        [Fact]
        public async Task GetPendingDoctorsAsync_WhenDoctorsPending_ReturnsMappedDoctors()
        {
            IReadOnlyList<DoctorProfile> pending =
            [
                new DoctorProfile
                {
                    Id = 5,
                    ConsultationFee = 250,
                    User = new ApplicationUser { FirstName = "Mona", LastName = "Adel" },
                    Specialty = new Specialty { Name = "Cardiology" }
                }
            ];
            _uow.Doctors.GetPendingApprovalsAsync(_ct).Returns(pending);

            var result = await _sut.GetPendingDoctorsAsync(_ct);

            Assert.True(result.IsSuccess);
            var doctor = Assert.Single(result.Value);
            Assert.Equal(5, doctor.Id);
            Assert.Equal("Mona Adel", doctor.FullName);
            Assert.Equal("Cardiology", doctor.SpecialtyName);
            Assert.Equal(250, doctor.ConsultationFee);
        }

        [Fact]
        public async Task GetPendingDoctorsAsync_WhenNonePending_ReturnsEmptyList()
        {
            _uow.Doctors.GetPendingApprovalsAsync(_ct).Returns(Array.Empty<DoctorProfile>());

            var result = await _sut.GetPendingDoctorsAsync(_ct);

            Assert.True(result.IsSuccess);
            Assert.Empty(result.Value);
        }
    }
}
