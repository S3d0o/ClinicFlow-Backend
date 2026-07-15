namespace Persistence.Repositories
{
    public class AdminRepo(ClinicDbContext context) : IAdminRepo
    {
        public async Task<(int totalDoctors, int totalPatients, int totalAppointments, int totalSpecialties, int pendingApprovals, int totalReviews)> GetOverviewStatsAsync(CancellationToken ct)
        {
            var totalDoctors = await context.DoctorProfiles.CountAsync(ct);
            var totalPatients = await context.PatientProfiles.CountAsync(ct);
            var totalAppointments = await context.Appointments.CountAsync(ct);
            var totalSpecialties = await context.Specialties.CountAsync(ct);
            var pendingApprovals = await context.DoctorProfiles.CountAsync(d => d.IsApprovedByAdmin == false, ct);
            var totalReviews = await context.Reviews.CountAsync(ct);

            return (totalDoctors, totalPatients, totalAppointments, totalSpecialties, pendingApprovals, totalReviews);
        }
    }
}
