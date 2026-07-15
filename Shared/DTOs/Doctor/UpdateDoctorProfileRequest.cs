namespace Shared.DTOs.Doctor
{
    public record UpdateDoctorProfileRequest
    {
        public string? Bio { get; init; }             
        public decimal ConsultationFee { get; init; }
        public string? ClinicAddress { get; init; }
        public string? ClinicCity { get; init; }
    }
}
