using Domain.Entities.Subscriptions;
using Domain.ResultPattern;
using NSubstitute;
using Shared.Authorization;
using Shared.DTOs.Entitlements;

namespace ClinicFlow.Tests.Services.SubscriptionServiceTests
{
    public class ChangePlanAsyncTests : SubscriptionServiceTestsBase
    {
        [Fact]
        public async Task ChangePlanAsync_KnownPlan_MovesTheSubscriptionAndSaves()
        {
            var subscription = OnPlan(Full);
            _subscriptions.GetPlanByCodeAsync("Starter", Arg.Any<CancellationToken>()).Returns(Starter);

            var result = await _sut.ChangePlanAsync(new ChangePlanRequest("Starter"), CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.Equal(Starter.Id, subscription.PlanId);
            Assert.Same(Starter, subscription.Plan);
            await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task ChangePlanAsync_DropsOverridesTheNewPlanMakesRedundant()
        {
            OnPlan(Starter);
            _subscriptions.GetPlanByCodeAsync("Clinic", Arg.Any<CancellationToken>()).Returns(Clinic);
            var billingExtra = WithOverride(Modules.Billing, enabled: true);      // Clinic includes it: no longer an extra
            var recruitmentExtra = WithOverride(Modules.Recruitment, enabled: true); // still an extra
            var chatbotOff = WithOverride(Modules.Chatbot, enabled: false);       // Clinic doesn't include it: nothing to switch off
            var frontDeskOff = WithOverride(Modules.FrontDesk, enabled: false);   // still switched off within the plan

            await _sut.ChangePlanAsync(new ChangePlanRequest("Clinic"), CancellationToken.None);

            _subscriptions.Received(1).RemoveOverride(billingExtra);
            _subscriptions.Received(1).RemoveOverride(chatbotOff);
            _subscriptions.DidNotReceive().RemoveOverride(recruitmentExtra);
            _subscriptions.DidNotReceive().RemoveOverride(frontDeskOff);
        }

        [Fact]
        public async Task ChangePlanAsync_UnknownPlan_ReturnsNotFound()
        {
            OnPlan(Full);

            var result = await _sut.ChangePlanAsync(new ChangePlanRequest("Gold"), CancellationToken.None);

            Assert.True(result.IsFailure);
            Assert.Equal("Subscription.PlanNotFound", result.Errors[0].Code);
            Assert.Equal(ErrorType.NotFound, result.Errors[0].Type);
            await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task ChangePlanAsync_NoSubscription_ReturnsNotFound()
        {
            _subscriptions.GetPlanByCodeAsync("Clinic", Arg.Any<CancellationToken>()).Returns(Clinic);
            _subscriptions.GetCurrentAsync(Arg.Any<CancellationToken>()).Returns((OrganizationSubscription?)null);

            var result = await _sut.ChangePlanAsync(new ChangePlanRequest("Clinic"), CancellationToken.None);

            Assert.Equal("Subscription.NotFound", result.Errors[0].Code);
            await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task ChangePlanAsync_SwitchingDisabled_ReturnsForbiddenWithoutReading()
        {
            _settings.AllowPlanSwitching = false;

            var result = await _sut.ChangePlanAsync(new ChangePlanRequest("Clinic"), CancellationToken.None);

            Assert.Equal("Subscription.SwitchingDisabled", result.Errors[0].Code);
            Assert.Equal(ErrorType.Forbidden, result.Errors[0].Type);
            await _subscriptions.DidNotReceiveWithAnyArgs().GetPlanByCodeAsync(default!, default);
            await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        }
    }
}
