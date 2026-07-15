namespace Persistence.AppData.Configurations
{
    internal class SpecialtyConfig : IEntityTypeConfiguration<Specialty>
    {
        public void Configure(EntityTypeBuilder<Specialty> builder)
        {
            builder.HasIndex(s => s.Name).IsUnique();
            builder.Property(s => s.Name).HasMaxLength(100);
        }
    }
}
