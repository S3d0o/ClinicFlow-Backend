using Shared.DTOs.Organization;

namespace Presentation.Validators
{
    // API_CONTRACT.md §13.23 OrganizationSettings: the bounds of each number setting, checked only when it is sent.
    // The key of a failure is the setting's name. The defaults are OrganizationSettingsResponse.Defaults
    public class OrganizationSettingsPatchRequestValidator : AbstractValidator<OrganizationSettingsPatchRequest>
    {
        public OrganizationSettingsPatchRequestValidator()
        {
            RuleFor(x => x.CancellationWindowHours).InclusiveBetween(0, 72);
            RuleFor(x => x.RescheduleWindowHours).InclusiveBetween(0, 72);
            RuleFor(x => x.NoShowGraceMinutes).InclusiveBetween(5, 120);
            RuleFor(x => x.WalkInCap).InclusiveBetween(0, 50);
            RuleFor(x => x.NoShowFee).InclusiveBetween(0, 10_000);
            RuleFor(x => x.DepositAmount).InclusiveBetween(0, 10_000);
            RuleFor(x => x.ReceptionistDiscountCapPercent).InclusiveBetween(0, 100);
            RuleFor(x => x.AccountantDiscountCapPercent).InclusiveBetween(0, 100);
            RuleFor(x => x.RefundApprovalThreshold).InclusiveBetween(0, 1_000_000);
            RuleFor(x => x.CashVarianceThreshold).InclusiveBetween(0, 100_000);
            RuleFor(x => x.ApplicationFileRetentionMonths).InclusiveBetween(1, 60);
            RuleFor(x => x.ReapplyCooldownDays).InclusiveBetween(0, 365);
        }
    }
}
