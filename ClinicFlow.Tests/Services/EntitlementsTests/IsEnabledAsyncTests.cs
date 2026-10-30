using NSubstitute;
using Shared.Authorization;
using Shared.DTOs.Entitlements;

namespace ClinicFlow.Tests.Services.EntitlementsTests
{
    public class IsEnabledAsyncTests : EntitlementsTestsBase
    {
        [Fact]
        public async Task IsEnabledAsync_CoreModule_IsTrueWithoutReadingTheSubscription()
        {
            Assert.True(await _sut.IsEnabledAsync(Modules.Appointments, CancellationToken.None));

            await _queries.DidNotReceiveWithAnyArgs().GetCurrentOrganizationAsync(default);
        }

        [Theory]
        [InlineData(Modules.FrontDesk, true)]
        [InlineData(Modules.Billing, true)]
        [InlineData(Modules.ClinicalRecords, false)]
        [InlineData(Modules.Insurance, false)]
        public async Task IsEnabledAsync_AddOn_FollowsThePlan(string module, bool expected)
        {
            OnClinicPlan();

            Assert.Equal(expected, await _sut.IsEnabledAsync(module, CancellationToken.None));
        }

        [Fact]
        public async Task IsEnabledAsync_AppliesOverridesAndDependencies()
        {
            OnClinicPlan(new ModuleOverrideData(Modules.FrontDesk, false), new ModuleOverrideData(Modules.Insurance, true));

            Assert.False(await _sut.IsEnabledAsync(Modules.FrontDesk, CancellationToken.None));
            Assert.True(await _sut.IsEnabledAsync(Modules.Insurance, CancellationToken.None));
            Assert.True(await _sut.IsEnabledAsync(Modules.ClinicalRecords, CancellationToken.None)); // INS needs EMR
        }

        [Fact]
        public async Task IsEnabledAsync_ReadsTheSubscriptionOncePerOrganization()
        {
            OnClinicPlan();

            await _sut.IsEnabledAsync(Modules.FrontDesk, CancellationToken.None);
            await _sut.IsEnabledAsync(Modules.Billing, CancellationToken.None);
            await _sut.EnabledModulesAsync(CancellationToken.None);

            await _queries.Received(1).GetCurrentOrganizationAsync(Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task IsEnabledAsync_AfterTheOrganizationIsInvalidated_ReadsAgain()
        {
            OnClinicPlan();
            await _sut.IsEnabledAsync(Modules.FrontDesk, CancellationToken.None);

            _cache.Invalidate(OrganizationId);
            OnClinicPlan(new ModuleOverrideData(Modules.FrontDesk, false));

            Assert.False(await _sut.IsEnabledAsync(Modules.FrontDesk, CancellationToken.None));
            await _queries.Received(2).GetCurrentOrganizationAsync(Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task IsEnabledAsync_NoSubscription_OnlyCoreModules()
        {
            _queries.GetCurrentOrganizationAsync(Arg.Any<CancellationToken>()).Returns(OrganizationEntitlementsData.None);

            Assert.False(await _sut.IsEnabledAsync(Modules.FrontDesk, CancellationToken.None));
            Assert.True(await _sut.IsEnabledAsync(Modules.Scheduling, CancellationToken.None));
        }

        [Fact]
        public async Task IsEnabledAsync_UnknownModule_Throws()
            // A typo must not quietly switch a feature off (or on)
            => await Assert.ThrowsAsync<ArgumentException>(() => _sut.IsEnabledAsync("Billing", CancellationToken.None));
    }
}
