using ClinicFlow.Domain.Enums;

namespace Shared.DTOs.Patient
{
    public record UpdatePatientProfileRequest
    {
        public BloodType? BloodType { get; init; }
        public string? Allergies { get; init; }
        public string? ChronicConditions { get; init; }
        public string? EmergencyContactName { get; init; }
        public string? EmergencyContactPhone { get; init; }
    }
}
