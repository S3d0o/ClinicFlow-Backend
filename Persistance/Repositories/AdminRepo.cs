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
            // Same filter as GET /api/admin/doctors/pending: a suspended applicant isn't waiting for approval (REVIEW.md NL23)
            var pendingApprovals = await context.DoctorProfiles.CountAsync(d => !d.IsApprovedByAdmin && d.User.IsActive, ct);
            var totalReviews = await context.Reviews.CountAsync(ct);

            return (totalDoctors, totalPatients, totalAppointments, totalSpecialties, pendingApprovals, totalReviews);
        }
    }
}
