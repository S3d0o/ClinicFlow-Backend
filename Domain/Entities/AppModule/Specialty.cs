namespace Domain.Entities.AppModule
{
    public class Specialty : BaseEntity<int>
    {
        public string Name { get; set; } = string.Empty; // unique — enforced in EF config
        public string? Description { get; set; }
        public string? IconUrl { get; set; }
        public bool IsActive { get; set; } = true;

        public ICollection<DoctorProfile> DoctorProfiles { get; set; } = [];
    }
}
