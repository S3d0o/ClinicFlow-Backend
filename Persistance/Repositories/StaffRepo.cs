namespace Persistence.Repositories
{
    public class StaffRepo(ClinicDbContext context) : IStaffRepo
    {
        public Task<StaffMember?> GetByUserIdAsync(Guid userId, CancellationToken ct)
            => context.StaffMembers.AsNoTracking().Include(s => s.Assignments).FirstOrDefaultAsync(s => s.UserId == userId, ct);

        public void Add(StaffMember member) => context.StaffMembers.Add(member);
    }
}
