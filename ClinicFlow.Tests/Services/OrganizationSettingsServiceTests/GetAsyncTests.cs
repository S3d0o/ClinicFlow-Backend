using NSubstitute;
using Services.Implementations;
using Shared.DTOs.Organization;

namespace ClinicFlow.Tests.Services.OrganizationSettingsServiceTests
{
    public class GetAsyncTests : OrganizationSettingsServiceTestsBase
    {
        [Fact]
        public async Task GetAsync_NothingStored_ReturnsTheDefaults()
            => Assert.Equal(OrganizationSettingsResponse.Defaults, await _sut.GetAsync(CancellationToken.None));

        [Fact]
        public async Task GetAsync_StoredKeys_OverrideTheirDefaultsOnly()
        {
            Store("cancellationWindowHours", "24");
            Store("depositEnabled", "true");
            Store("depositAmount", "150.5");

            var settings = await _sut.GetAsync(CancellationToken.None);

            Assert.Equal(OrganizationSettingsResponse.Defaults with
            {
                CancellationWindowHours = 24, DepositEnabled = true, DepositAmount = 150.5m
            }, settings);
            Assert.Equal(TimeSpan.FromHours(24), settings.CancellationWindow);
        }

        [Fact]
        public async Task GetAsync_AStoredKeyTheSettingsNoLongerHave_IsIgnored()
        {
            Store("retiredSetting", "\"x\"");

            Assert.Equal(OrganizationSettingsResponse.Defaults, await _sut.GetAsync(CancellationToken.None));
        }

        [Fact]
        public async Task GetAsync_Twice_ReadsTheDatabaseOnce()
        {
            await _sut.GetAsync(CancellationToken.None);
            await _sut.GetAsync(CancellationToken.None);

            await _uow.OrganizationSettings.Received(1).GetAllAsync(Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GetAsync_AfterTheCacheLifetime_ReadsAgain()
        {
            await _sut.GetAsync(CancellationToken.None);
            _clock.UtcNow.Returns(Now + OrganizationSettingsCache.Lifetime);

            await _sut.GetAsync(CancellationToken.None);

            await _uow.OrganizationSettings.Received(2).GetAllAsync(Arg.Any<CancellationToken>());
        }
    }
}
