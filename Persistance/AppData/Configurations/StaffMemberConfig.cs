namespace Persistence.AppData.Configurations
{
    internal class StaffMemberConfig : IEntityTypeConfiguration<StaffMember>
    {
        public void Configure(EntityTypeBuilder<StaffMember> builder)
        {
            builder.ToTable("StaffMembers");
            builder.IsAudited("StaffMember"); // FR-AUD-01: who works here decides who can do what
            builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);
            builder.HasIndex(s => new { s.OrganizationId, s.UserId }).IsUnique();
            builder.HasOne(s => s.User).WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Restrict);
            builder.HasMany(s => s.Assignments).WithOne().HasForeignKey(a => a.StaffMemberId).OnDelete(DeleteBehavior.Cascade);
        }
    }
}
