using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Persistence.AppData.Configurations
{
    public class ApplicationUserConfig : IEntityTypeConfiguration<ApplicationUser>
    {
        public void Configure(EntityTypeBuilder<ApplicationUser> builder)
        {
            builder.Property(a=>a.Gender).HasConversion<string>();
        }
    }
}
