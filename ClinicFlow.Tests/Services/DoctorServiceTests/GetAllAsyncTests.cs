using Domain.Entities.AppModule;
using Domain.Entities.IdentityModule;
using Domain.Enums;
using Domain.Parameters;
using NSubstitute;
using Shared.DTOs.Doctor;

namespace ClinicFlow.Tests.Services.DoctorServiceTests
{
    public class GetAllAsyncTests : DoctorServiceTestsBase
    {
        private readonly CancellationToken _ct = new CancellationTokenSource().Token;

        [Fact]
        public async Task GetAllAsync_Always_PassesEveryFilterToTheApprovedDoctorsQuery()
        {
            DoctorFilterParams? passed = null;
            _uow.Doctors.GetApprovedPagedAsync(Arg.Do<DoctorFilterParams>(p => passed = p), _ct)
                .Returns((new List<DoctorProfile>(), 0));

            await _sut.GetAllAsync(new DoctorFilterRequest
            {
                Name = "Ahmed", SpecialtyId = 3, City = "Cairo", PageNumber = 2, PageSize = 5, SortBy = DoctorSortBy.Fee
            }, _ct);

            Assert.NotNull(passed);
            Assert.Equal("Ahmed", passed!.Name);
            Assert.Equal(3, passed.SpecialtyId);
            Assert.Equal("Cairo", passed.City);
            Assert.Equal(2, passed.PageNumber);
            Assert.Equal(5, passed.PageSize);
            Assert.Equal(DoctorSortBy.Fee, passed.SortBy);
        }

        [Fact]
        public async Task GetAllAsync_WhenDoctorsFound_MapsSummariesInQueryOrder()
        {
            _uow.Doctors.GetApprovedPagedAsync(Arg.Any<DoctorFilterParams>(), _ct).Returns((new List<DoctorProfile>
            {
                Doctor(5, "Ahmed", "Elsayed", "Cardiology", 450m, 4.8, "Cairo"),
                Doctor(9, "Nour", "Hassan", "Dermatology", 300m, 4.1, null)
            }, 2));

            var result = await _sut.GetAllAsync(new DoctorFilterRequest(), _ct);

            Assert.True(result.IsSuccess);
            Assert.Equal(new[]
            {
                new DoctorSummaryResponse { Id = 5, FullName = "Ahmed Elsayed", SpecialtyName = "Cardiology", YearsOfExperience = 10, ConsultationFee = 450m, AverageRating = 4.8, ClinicCity = "Cairo" },
                new DoctorSummaryResponse { Id = 9, FullName = "Nour Hassan", SpecialtyName = "Dermatology", YearsOfExperience = 10, ConsultationFee = 300m, AverageRating = 4.1, ClinicCity = null }
            }, result.Value);
        }

        [Fact]
        public async Task GetAllAsync_WhenNoDoctorsMatch_ReturnsEmptyList()
        {
            _uow.Doctors.GetApprovedPagedAsync(Arg.Any<DoctorFilterParams>(), _ct).Returns((new List<DoctorProfile>(), 0));

            var result = await _sut.GetAllAsync(new DoctorFilterRequest { City = "Nowhere" }, _ct);

            Assert.True(result.IsSuccess);
            Assert.Empty(result.Value);
        }

        private static DoctorProfile Doctor(int id, string first, string last, string specialty, decimal fee, double rating, string? city) => new()
        {
            Id = id,
            User = new ApplicationUser { FirstName = first, LastName = last },
            Specialty = new Specialty { Name = specialty },
            YearsOfExperience = 10,
            ConsultationFee = fee,
            AverageRating = rating,
            ClinicCity = city
        };
    }
}
