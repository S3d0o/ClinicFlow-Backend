using Shared.DTOs.Patient;

namespace Services.Implementations
{
    public class PatientService(
        IUnitOfWork uow,
        ILogger<PatientService> logger,
        IMapper mapper) : IPatientService
    {
        public async Task<Result<PatientProfileResponse>> GetProfileAsync(Guid userId, CancellationToken ct)
        {
            var patient = await uow.Patients.GetPatientByUserIdAsync(userId, ct);
            if (patient == null)
            {
                logger.LogWarning("Patient profile not found for userId: {UserId}", userId);
                return PatientErrors.ProfileNotFound(userId);
            }
            return mapper.Map<PatientProfileResponse>(patient);
        }

        public async Task<Result> UpdateProfileAsync(Guid userId, UpdatePatientProfileRequest request, CancellationToken ct)
        {
           var patient = await uow.Patients.GetPatientByUserIdAsync(userId, ct);
            if (patient == null)
            {
                logger.LogWarning("Patient profile not found for userId: {UserId}", userId);
                return PatientErrors.ProfileNotFound(userId);
            }
            mapper.Map(request, patient);
            await uow.SaveChangesAsync(ct);
            return Result.Ok();
        }
    }
}
