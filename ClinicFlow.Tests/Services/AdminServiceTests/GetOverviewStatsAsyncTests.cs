using NSubstitute;

namespace ClinicFlow.Tests.Services.AdminServiceTests
{
    public class GetOverviewStatsAsyncTests : AdminServiceTestsBase
    {
        private readonly CancellationToken _ct = CancellationToken.None;

        [Fact]
        public async Task GetOverviewStatsAsync_WhenCalled_MapsEachCountToItsField()
        {
            // Distinct values so a swapped tuple position is caught
            _uow.Admins.GetOverviewStatsAsync(_ct).Returns((1, 2, 3, 4, 5, 6));

            var result = await _sut.GetOverviewStatsAsync(_ct);

            Assert.True(result.IsSuccess);
            Assert.Equal(1, result.Value.TotalDoctors);
            Assert.Equal(2, result.Value.TotalPatients);
            Assert.Equal(3, result.Value.TotalAppointments);
            Assert.Equal(4, result.Value.TotalSpecialties);
            Assert.Equal(5, result.Value.PendingApprovals);
            Assert.Equal(6, result.Value.TotalReviews);
        }
    }
}
