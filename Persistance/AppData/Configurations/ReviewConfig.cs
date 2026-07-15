namespace Persistence.AppData.Configurations
{
    internal class ReviewConfig : IEntityTypeConfiguration<Review>
    {
        public void Configure(EntityTypeBuilder<Review> builder)
        {
            builder.ToTable("Reviews",t => t.HasCheckConstraint("CK_Review_Rating", "[Rating] BETWEEN 1 AND 5")); // Ensure rating is between 1 and 5

            builder.Property(r => r.Comment).HasMaxLength(300);

        }
    }
}
