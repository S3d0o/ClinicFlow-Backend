using NSubstitute;
using Shared.Authorization;
using Shared.DTOs.Entitlements;

namespace ClinicFlow.Tests.Services.EntitlementsTests
{
    public class LimitAsyncTests : EntitlementsTestsBase
    {
        [Fact]
        public async Task LimitAsync_IsThePlansValue()
        {
            OnClinicPlan();

            Assert.Equal(5, await _sut.LimitAsync(Limits.Doctors, CancellationToken.None));
            Assert.Equal(1, await _sut.LimitAsync(Limits.Branches, CancellationToken.None));
        }

        [Fact]
        public async Task LimitAsync_NotSetByThePlan_IsNull()
        {
            OnClinicPlan();

            Assert.Null(await _sut.LimitAsync(Limits.PhoneMessagesPerMonth, CancellationToken.None));
        }

        [Fact]
        public async Task LimitAsync_NoSubscription_IsNull()
        {
            _queries.GetCurrentOrganizationAsync(Arg.Any<CancellationToken>()).Returns(OrganizationEntitlementsData.None);

            Assert.Null(await _sut.LimitAsync(Limits.Doctors, CancellationToken.None));
        }

        [Fact]
        public async Task LimitAsync_UnknownKey_Throws()
            => await Assert.ThrowsAsync<ArgumentException>(() => _sut.LimitAsync("Doctors", CancellationToken.None));
    }
}
