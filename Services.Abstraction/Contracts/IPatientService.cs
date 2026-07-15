using Shared.DTOs.Patient;

namespace Services.Abstraction.Contracts
{
    public interface IPatientService
    {
        public Task<Result<PatientProfileResponse>> GetProfileAsync(Guid userId, CancellationToken ct);
        public Task<Result> UpdateProfileAsync(Guid userId, UpdatePatientProfileRequest request, CancellationToken ct);

    }
}
