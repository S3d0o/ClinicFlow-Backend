using Shared.Enums;

namespace Shared.DTOs.Auth
{
    public record RegisterDoctorRequest(
    string FirstName,
    string LastName,
    string Email,
    string Password,
    string PhoneNumber,
    Gender Gender,
    DateOnly? DateOfBirth,
    // Doctor-specific
    int SpecialtyId,
    string? Bio,
    int YearsOfExperience,
    decimal ConsultationFee,
    string? ClinicAddress,
    string? ClinicCity);
}
