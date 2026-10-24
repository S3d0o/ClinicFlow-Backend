using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Persistence.AppData.Configurations
{
    public class ApplicationUserConfig : IEntityTypeConfiguration<ApplicationUser>
    {
        public void Configure(EntityTypeBuilder<ApplicationUser> builder)
        {
            builder.Property(a=>a.Gender).HasConversion<string>();

            // Only suspension is audited (P0): the rest of the row is Identity's bookkeeping (stamps, lockout
            // counters, the password hash) or a person's own details
            builder.IsAudited("User", AuditMode.Ignored);
            builder.Property(a => a.IsActive).IsAudited();
        }
    }
}
