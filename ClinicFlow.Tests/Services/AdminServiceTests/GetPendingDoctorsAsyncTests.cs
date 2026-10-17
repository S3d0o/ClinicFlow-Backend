using Domain.Entities.AppModule;
using Domain.Entities.IdentityModule;
using NSubstitute;
using Shared.DTOs.Admin;

namespace ClinicFlow.Tests.Services.AdminServiceTests
{
    public class GetPendingDoctorsAsyncTests : AdminServiceTestsBase
    {
        private readonly CancellationToken _ct = CancellationToken.None;

        [Fact]
        public async Task GetPendingDoctorsAsync_WhenDoctorsPending_ReturnsMappedDoctorsAndTheTotal()
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
            _uow.Doctors.GetPendingApprovalsAsync(1, null, _ct).Returns((pending, 1));

            var result = await _sut.GetPendingDoctorsAsync(new PendingDoctorsFilterRequest(), _ct);

            Assert.True(result.IsSuccess);
            Assert.Equal(1, result.Value.TotalCount);
            var doctor = Assert.Single(result.Value.Items);
            Assert.Equal(5, doctor.Id);
            Assert.Equal("Mona Adel", doctor.FullName);
            Assert.Equal("Cardiology", doctor.SpecialtyName);
            Assert.Equal(250, doctor.ConsultationFee);
        }

        [Fact]
        public async Task GetPendingDoctorsAsync_WithPageSize_PassesThePageToTheRepository()
        {
            // REVIEW.md NL2: paged only when the caller asks for it
            _uow.Doctors.GetPendingApprovalsAsync(2, 10, _ct).Returns((Array.Empty<DoctorProfile>(), 12));

            var result = await _sut.GetPendingDoctorsAsync(new PendingDoctorsFilterRequest { PageNumber = 2, PageSize = 10 }, _ct);

            Assert.Equal(12, result.Value.TotalCount);
            await _uow.Doctors.Received(1).GetPendingApprovalsAsync(2, 10, _ct);
        }

        [Fact]
        public async Task GetPendingDoctorsAsync_WhenNonePending_ReturnsEmptyList()
        {
            _uow.Doctors.GetPendingApprovalsAsync(1, null, _ct).Returns((Array.Empty<DoctorProfile>(), 0));

            var result = await _sut.GetPendingDoctorsAsync(new PendingDoctorsFilterRequest(), _ct);

            Assert.True(result.IsSuccess);
            Assert.Empty(result.Value.Items);
        }
    }
}
