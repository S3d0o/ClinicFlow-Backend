using System.Text.Json.Serialization;

namespace Shared.DTOs.Organization
{
    // GET /api/v2/organization/settings (API_CONTRACT.md §13.23 OrganizationSettings; FR-ORG-05): every key, with the
    // default filled in for one never saved. Money is EGP. Defaults lives here and nowhere else; the bounds are in
    // Presentation's OrganizationSettingsPatchRequestValidator
    public sealed record OrganizationSettingsResponse(
        int CancellationWindowHours,
        int RescheduleWindowHours,
        int NoShowGraceMinutes,
        int WalkInCap,
        decimal NoShowFee,
        bool DepositEnabled,
        decimal DepositAmount,
        decimal ReceptionistDiscountCapPercent,
        decimal AccountantDiscountCapPercent,
        decimal RefundApprovalThreshold,
        decimal CashVarianceThreshold,
        bool ShareRecordsWithinBranch,
        int ApplicationFileRetentionMonths,
        int ReapplyCooldownDays)
    {
        public static readonly OrganizationSettingsResponse Defaults = new(
            CancellationWindowHours: 2,
            RescheduleWindowHours: 2,
            NoShowGraceMinutes: 15,
            WalkInCap: 5,
            NoShowFee: 0,
            DepositEnabled: false,
            DepositAmount: 100,
            ReceptionistDiscountCapPercent: 10,
            AccountantDiscountCapPercent: 20,
            RefundApprovalThreshold: 1000,
            CashVarianceThreshold: 50,
            ShareRecordsWithinBranch: true,
            ApplicationFileRetentionMonths: 6,
            ReapplyCooldownDays: 90);

        // What the appointment rules take (Appointment.Cancel, RescheduleTo, MarkNoShow)
        [JsonIgnore] public TimeSpan CancellationWindow => TimeSpan.FromHours(CancellationWindowHours);
        [JsonIgnore] public TimeSpan RescheduleWindow => TimeSpan.FromHours(RescheduleWindowHours);
        [JsonIgnore] public TimeSpan NoShowGrace => TimeSpan.FromMinutes(NoShowGraceMinutes);
    }

    // PATCH /api/v2/organization/settings: only the keys to change. An unknown key is a 400, not ignored
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record OrganizationSettingsPatchRequest(
        int? CancellationWindowHours = null,
        int? RescheduleWindowHours = null,
        int? NoShowGraceMinutes = null,
        int? WalkInCap = null,
        decimal? NoShowFee = null,
        bool? DepositEnabled = null,
        decimal? DepositAmount = null,
        decimal? ReceptionistDiscountCapPercent = null,
        decimal? AccountantDiscountCapPercent = null,
        decimal? RefundApprovalThreshold = null,
        decimal? CashVarianceThreshold = null,
        bool? ShareRecordsWithinBranch = null,
        int? ApplicationFileRetentionMonths = null,
        int? ReapplyCooldownDays = null);
}
