namespace Shared.DTOs.Doctor
{
    public record DoctorResponse
    {
        public int Id { get; init; }
        public string FullName { get; init; } = string.Empty;
        public string SpecialtyName { get; init; } = string.Empty;
        public string? Bio { get; init; }
        public int YearsOfExperience { get; init; }
        public decimal ConsultationFee { get; init; }
        public double AverageRating { get; init; }
        public int TotalReviews { get; init; }
        public string? ClinicAddress { get; init; }
        public string? ClinicCity { get; init; }
    }
}
