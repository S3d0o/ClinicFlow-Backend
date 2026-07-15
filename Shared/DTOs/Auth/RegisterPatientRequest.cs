using ClinicFlow.Domain.Enums;
using Shared.Enums;

namespace Shared.DTOs.Auth
{
    public record RegisterPatientRequest(
    string FirstName,
    string LastName,
    string Email,
    string Password,
    string PhoneNumber,
    Gender Gender,
    DateOnly? DateOfBirth,
    // Patient-specific
    BloodType? BloodType,
    string? Allergies,
    string? ChronicConditions,
    string? EmergencyContactName,
    string? EmergencyContactPhone);
}
