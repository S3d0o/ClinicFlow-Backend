using Domain.Entities.Subscriptions;
using Domain.Enums;

namespace Persistence.AppData.Configurations
{
    // Organization-owned, so ClinicDbContext.ConfigureTenancy adds the filter and the foreign key to Organization.
    // Changes are audited (docs/v2/adr/0008 decision 6, FR-AUD-01)
    internal class OrganizationSubscriptionConfig : IEntityTypeConfiguration<OrganizationSubscription>
    {
        public void Configure(EntityTypeBuilder<OrganizationSubscription> builder)
        {
            builder.ToTable("OrganizationSubscriptions");
            builder.HasKey(s => s.OrganizationId); // one subscription per organization
            builder.Property(s => s.OrganizationId).ValueGeneratedNever();
            builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);

            builder.HasOne(s => s.Plan).WithMany().HasForeignKey(s => s.PlanId).OnDelete(DeleteBehavior.Restrict);

            builder.IsAudited("OrganizationSubscription");

            // V2's one organization keeps everything it can do today: the Full plan, plus recruitment below
            builder.HasData(new OrganizationSubscription
            {
                OrganizationId = Organization.DefaultId,
                PlanId = PlanSeed.FullId,
                Status = SubscriptionStatus.Active,
            });
        }
    }

    internal class OrganizationModuleOverrideConfig : IEntityTypeConfiguration<OrganizationModuleOverride>
    {
        public void Configure(EntityTypeBuilder<OrganizationModuleOverride> builder)
        {
            builder.ToTable("OrganizationModuleOverrides");
            builder.HasKey(o => new { o.OrganizationId, o.ModuleCode });
            builder.Property(o => o.ModuleCode).HasMaxLength(10);

            builder.IsAudited("OrganizationModuleOverride");

            builder.HasData(new OrganizationModuleOverride
            {
                OrganizationId = Organization.DefaultId,
                ModuleCode = Modules.Recruitment,
                Enabled = true,
            });
        }
    }
}
