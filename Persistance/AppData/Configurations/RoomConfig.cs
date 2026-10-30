namespace Persistence.AppData.Configurations
{
    internal class RoomConfig : IEntityTypeConfiguration<Room>
    {
        public void Configure(EntityTypeBuilder<Room> builder)
        {
            builder.ToTable("Rooms");
            builder.IsAudited("Room"); // FR-AUD-01, with the branches it belongs to
            builder.Property(r => r.NameEn).HasMaxLength(60);
            builder.Property(r => r.NameAr).HasMaxLength(60);
            builder.Property(r => r.Kind).HasConversion<string>().HasMaxLength(20);
            // Names are unique at a branch; RoomRepo.NameExistsAsync checks first so the API answers Room.NameTaken
            builder.HasIndex(r => new { r.BranchId, r.NameEn }).IsUnique();
            builder.HasIndex(r => new { r.BranchId, r.NameAr }).IsUnique();
        }
    }
}
