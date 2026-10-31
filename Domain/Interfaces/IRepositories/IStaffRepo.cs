namespace Domain.Interfaces.IRepositories
{
    public interface IStaffRepo
    {
        // The user's staff member with its assignments, untracked; null for a user who isn't staff (a patient)
        Task<StaffMember?> GetByUserIdAsync(Guid userId, CancellationToken ct);
        void Add(StaffMember member);
    }
}
