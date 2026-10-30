using Domain.Entities.Subscriptions;
using Domain.ResultPattern;
using NSubstitute;
using Shared.Authorization;
using Shared.DTOs.Entitlements;

namespace ClinicFlow.Tests.Services.SubscriptionServiceTests
{
    public class SetModuleAsyncTests : SubscriptionServiceTestsBase
    {
        [Fact]
        public async Task SetModuleAsync_AnExtraBeyondThePlan_AddsAnEnabledOverride()
        {
            OnPlan(Clinic);

            var result = await _sut.SetModuleAsync(new SetModuleRequest(Modules.Recruitment, true), CancellationToken.None);

            Assert.True(result.IsSuccess);
            _subscriptions.Received(1).AddOverride(Arg.Is<OrganizationModuleOverride>(o => o!.ModuleCode == Modules.Recruitment && o.Enabled));
            await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task SetModuleAsync_OffWithinThePlan_AddsADisabledOverride()
        {
            OnPlan(Clinic);

            await _sut.SetModuleAsync(new SetModuleRequest(Modules.FrontDesk, false), CancellationToken.None);

            _subscriptions.Received(1).AddOverride(Arg.Is<OrganizationModuleOverride>(o => o!.ModuleCode == Modules.FrontDesk && !o.Enabled));
        }

        [Fact]
        public async Task SetModuleAsync_BackToWhatThePlanSays_RemovesTheOverride()
        {
            OnPlan(Clinic);
            var frontDeskOff = WithOverride(Modules.FrontDesk, enabled: false);

            await _sut.SetModuleAsync(new SetModuleRequest(Modules.FrontDesk, true), CancellationToken.None);

            _subscriptions.Received(1).RemoveOverride(frontDeskOff);
            _subscriptions.DidNotReceiveWithAnyArgs().AddOverride(default!);
            await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task SetModuleAsync_AlreadyWhatThePlanSays_SavesNoOverride()
        {
            OnPlan(Clinic);

            var result = await _sut.SetModuleAsync(new SetModuleRequest(Modules.Billing, true), CancellationToken.None);

            Assert.True(result.IsSuccess);
            _subscriptions.DidNotReceiveWithAnyArgs().AddOverride(default!);
            _subscriptions.DidNotReceiveWithAnyArgs().RemoveOverride(default!);
        }

        [Fact]
        public async Task SetModuleAsync_AnExistingOverride_IsFlipped()
        {
            OnPlan(Starter);
            var recruitment = WithOverride(Modules.Recruitment, enabled: false);

            await _sut.SetModuleAsync(new SetModuleRequest(Modules.Recruitment, true), CancellationToken.None);

            Assert.True(recruitment.Enabled); // Starter doesn't include REC, so the extra stays an override
            _subscriptions.DidNotReceiveWithAnyArgs().AddOverride(default!);
        }

        [Fact]
        public async Task SetModuleAsync_OffWhileAnEnabledAddOnNeedsIt_ReturnsModuleRequired()
        {
            // Full includes INS, which needs BIL and EMR (ADR 0008 decision 4)
            OnPlan(Full);

            var result = await _sut.SetModuleAsync(new SetModuleRequest(Modules.Billing, false), CancellationToken.None);

            Assert.True(result.IsFailure);
            Assert.Equal("Subscription.ModuleRequired", result.Errors[0].Code);
            Assert.Equal(ErrorType.Conflict, result.Errors[0].Type);
            Assert.Contains(Modules.Insurance, result.Errors[0].Description);
            await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task SetModuleAsync_OffOnceTheAddOnThatNeededItIsOff_Succeeds()
        {
            OnPlan(Full);
            WithOverride(Modules.Insurance, enabled: false);

            var result = await _sut.SetModuleAsync(new SetModuleRequest(Modules.Billing, false), CancellationToken.None);

            Assert.True(result.IsSuccess);
        }

        [Fact]
        public async Task SetModuleAsync_ACoreModule_ReturnsValidation()
        {
            OnPlan(Clinic);

            var result = await _sut.SetModuleAsync(new SetModuleRequest(Modules.Appointments, false), CancellationToken.None);

            Assert.Equal("Subscription.NotAnAddOn", result.Errors[0].Code);
            Assert.Equal(ErrorType.Validation, result.Errors[0].Type);
            await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task SetModuleAsync_NoSubscription_ReturnsNotFound()
        {
            _subscriptions.GetCurrentAsync(Arg.Any<CancellationToken>()).Returns((OrganizationSubscription?)null);

            var result = await _sut.SetModuleAsync(new SetModuleRequest(Modules.Recruitment, true), CancellationToken.None);

            Assert.Equal("Subscription.NotFound", result.Errors[0].Code);
        }

        [Fact]
        public async Task SetModuleAsync_SwitchingDisabled_ReturnsForbidden()
        {
            _settings.AllowPlanSwitching = false;

            var result = await _sut.SetModuleAsync(new SetModuleRequest(Modules.Recruitment, true), CancellationToken.None);

            Assert.Equal("Subscription.SwitchingDisabled", result.Errors[0].Code);
            await _subscriptions.DidNotReceiveWithAnyArgs().GetCurrentAsync(default);
        }
    }
}
