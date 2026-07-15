using ClinicFlow.Domain.Enums;

namespace Shared.DTOs.Patient
{
    public record PatientProfileResponse
    {
        public int Id { get; init; }
        public string FullName { get; init; } = string.Empty;
        public string Email { get; init; } = string.Empty;
        public string PhoneNumber { get; init; } = string.Empty;
        public BloodType? BloodType { get; init; }
        public string? Allergies { get; init; }
        public string? ChronicConditions { get; init; }
        public string? EmergencyContactName { get; init; }
        public string? EmergencyContactPhone { get; init; }
    }
}
