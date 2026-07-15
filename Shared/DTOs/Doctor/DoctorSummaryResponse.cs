namespace Shared.DTOs.Doctor
{
    public record DoctorSummaryResponse
    {
        public int Id { get; init; }
        public string FullName { get; set; } = string.Empty;
        public string SpecialtyName { get; init; } = string.Empty;
        public int YearsOfExperience { get; init; }
        public decimal ConsultationFee { get; init; } 
        public double AverageRating { get; init; } = 0.0;
        public string? ClinicCity { get; init; }
    }
}
