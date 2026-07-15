namespace Shared.DTOs.Admin
{
    public record OverviewStatsResponse(
    int TotalDoctors,
    int TotalPatients,
    int TotalAppointments,
    int TotalSpecialties,
    int PendingApprovals,
    int TotalReviews);
}
