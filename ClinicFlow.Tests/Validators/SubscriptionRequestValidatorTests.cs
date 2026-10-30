using FluentValidation.TestHelper;
using Presentation.Validators;
using Shared.Authorization;
using Shared.DTOs.Entitlements;

namespace ClinicFlow.Tests.Validators
{
    // The platform switches' requests (docs/v2/adr/0008, plans/R1.md M1)
    public class SubscriptionRequestValidatorTests
    {
        private readonly ChangePlanRequestValidator _plan = new();
        private readonly SetModuleRequestValidator _module = new();

        [Fact]
        public void ChangePlan_ACode_IsValid()
            => _plan.TestValidate(new ChangePlanRequest("Clinic")).ShouldNotHaveAnyValidationErrors();

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("ThisPlanCodeIsFarLongerThanThirtyTwoCharacters")]
        public void ChangePlan_EmptyOrTooLongCode_Fails(string code)
            => _plan.TestValidate(new ChangePlanRequest(code)).ShouldHaveValidationErrorFor(x => x.PlanCode);

        [Theory]
        [InlineData(Modules.FrontDesk)]
        [InlineData(Modules.Recruitment)]
        [InlineData(Modules.Chatbot)]
        public void SetModule_AnAddOn_IsValid(string module)
            => _module.TestValidate(new SetModuleRequest(module, true)).ShouldNotHaveAnyValidationErrors();

        [Theory]
        [InlineData(Modules.Appointments)] // core modules are always on
        [InlineData("fd")]                 // codes are exact
        [InlineData("")]
        public void SetModule_NotAnAddOn_Fails(string module)
            => _module.TestValidate(new SetModuleRequest(module, false)).ShouldHaveValidationErrorFor(x => x.Module);
    }
}
