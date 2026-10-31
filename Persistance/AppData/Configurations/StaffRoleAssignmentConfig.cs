namespace Persistence.AppData.Configurations
{
    internal class StaffRoleAssignmentConfig : IEntityTypeConfiguration<StaffRoleAssignment>
    {
        public void Configure(EntityTypeBuilder<StaffRoleAssignment> builder)
        {
            builder.ToTable("StaffRoleAssignments");
            builder.IsAudited("StaffRoleAssignment"); // FR-STF-03: role changes are audited before and after
            builder.Property(a => a.Role).HasMaxLength(20);
            // Optional, unlike IBranchOwned's: no branch means the whole organization
            builder.HasOne<Branch>().WithMany().HasForeignKey(a => a.BranchId).OnDelete(DeleteBehavior.Restrict);
            builder.HasIndex(a => new { a.StaffMemberId, a.Role, a.BranchId }).IsUnique();
        }
    }
}
