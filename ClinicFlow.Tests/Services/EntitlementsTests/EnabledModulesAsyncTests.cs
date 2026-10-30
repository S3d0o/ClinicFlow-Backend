using Shared.Authorization;
using Shared.DTOs.Entitlements;

namespace ClinicFlow.Tests.Services.EntitlementsTests
{
    public class EnabledModulesAsyncTests : EntitlementsTestsBase
    {
        [Fact]
        public async Task EnabledModulesAsync_IsTheCorePlusThePlanAndOverrides()
        {
            OnClinicPlan(new ModuleOverrideData(Modules.Recruitment, true));

            var modules = await _sut.EnabledModulesAsync(CancellationToken.None);

            Assert.Equal(
                Modules.Core.Concat([Modules.FrontDesk, Modules.Billing, Modules.PatientPortal, Modules.Reporting, Modules.Recruitment]).Order(),
                modules.Order());
        }
    }
}
