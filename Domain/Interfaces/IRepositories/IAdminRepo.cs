namespace Domain.Interfaces.IRepositories
{
    public interface IAdminRepo
    {
        Task<(int totalDoctors, int totalPatients, int totalAppointments, int totalSpecialties, int pendingApprovals, int totalReviews)> GetOverviewStatsAsync(CancellationToken ct);
    }
}
