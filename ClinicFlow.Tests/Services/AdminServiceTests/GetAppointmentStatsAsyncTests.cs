using NSubstitute;

namespace ClinicFlow.Tests.Services.AdminServiceTests
{
    public class GetAppointmentStatsAsyncTests : AdminServiceTestsBase
    {
        private readonly CancellationToken _ct = CancellationToken.None;

        [Fact]
        public async Task GetAppointmentStatsAsync_WhenCalled_MapsEachCountToItsStatus()
        {
            // Distinct values so a swapped tuple position is caught
            _uow.Appointments.GetStatsAsync(_ct).Returns((1, 2, 3, 4, 5));

            var result = await _sut.GetAppointmentStatsAsync(_ct);

            Assert.True(result.IsSuccess);
            Assert.Equal(1, result.Value.Pending);
            Assert.Equal(2, result.Value.Confirmed);
            Assert.Equal(3, result.Value.Completed);
            Assert.Equal(4, result.Value.Cancelled);
            Assert.Equal(5, result.Value.NoShow);
        }
    }
}
