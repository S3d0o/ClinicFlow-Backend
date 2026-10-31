using Domain.Entities.Tenancy;
using NSubstitute;
using Shared.DTOs.Organization;

namespace ClinicFlow.Tests.Services.OrganizationSettingsServiceTests
{
    public class UpdateAsyncTests : OrganizationSettingsServiceTestsBase
    {
        [Fact]
        public async Task UpdateAsync_ANewKey_AddsItsRowAndReturnsEverySetting()
        {
            var result = await _sut.UpdateAsync(new OrganizationSettingsPatchRequest(CancellationWindowHours: 24), CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.Equal(OrganizationSettingsResponse.Defaults with { CancellationWindowHours = 24 }, result.Value);
            var row = Assert.Single(_stored);
            Assert.Equal(("cancellationWindowHours", "24"), (row.Key, row.Value));
            await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
            await _transaction.Received(1).CommitAsync(Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task UpdateAsync_AStoredKey_ChangesItsRow()
        {
            Store("walkInCap", "8");

            await _sut.UpdateAsync(new OrganizationSettingsPatchRequest(WalkInCap: 12), CancellationToken.None);

            Assert.Equal("12", Assert.Single(_stored).Value);
            _uow.OrganizationSettings.DidNotReceive().Add(Arg.Any<OrganizationSetting>());
        }

        [Fact]
        public async Task UpdateAsync_KeysNotSent_AreLeftAlone()
        {
            Store("walkInCap", "8");

            var result = await _sut.UpdateAsync(new OrganizationSettingsPatchRequest(DepositEnabled: true), CancellationToken.None);

            Assert.Equal((8, true), (result.Value.WalkInCap, result.Value.DepositEnabled));
            Assert.Equal(2, _stored.Count);
        }

        [Fact]
        public async Task UpdateAsync_TheCurrentValue_WritesNothing()
        {
            // The default, never stored, and a stored value sent again: no row and no audit entry
            Store("walkInCap", "8");

            await _sut.UpdateAsync(new OrganizationSettingsPatchRequest(WalkInCap: 8, NoShowGraceMinutes: 15), CancellationToken.None);

            Assert.Equal(("walkInCap", "8"), (Assert.Single(_stored).Key, _stored[0].Value));
            _uow.OrganizationSettings.DidNotReceive().Add(Arg.Any<OrganizationSetting>());
        }

        [Fact]
        public async Task UpdateAsync_AnAmountWithTrailingZeros_IsTheSameAmount()
        {
            Store("depositAmount", "150");

            await _sut.UpdateAsync(new OrganizationSettingsPatchRequest(DepositAmount: 150.00m, NoShowFee: 25.50m), CancellationToken.None);

            Assert.Equal(["150", "25.5"], _stored.Select(s => s.Value));
        }

        [Fact]
        public async Task UpdateAsync_DropsTheCachedSettings_SoTheNextReadSeesTheChange()
        {
            Assert.Equal(2, (await _sut.GetAsync(CancellationToken.None)).CancellationWindowHours); // cached

            await _sut.UpdateAsync(new OrganizationSettingsPatchRequest(CancellationWindowHours: 6), CancellationToken.None);

            Assert.Equal(6, (await _sut.GetAsync(CancellationToken.None)).CancellationWindowHours);
        }
    }
}
