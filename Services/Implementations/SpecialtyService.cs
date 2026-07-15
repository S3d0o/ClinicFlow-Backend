
using Shared.DTOs.Specialty;

namespace Services.Implementations
{
    public class SpecialtyService(
        IUnitOfWork uow,
        IMapper mapper,
        ILogger<SpecialtyService> logger) : ISpecialtyService
    {
        public async Task<Result<SpecialtyResponse>> CreateAsync(SpecialtyRequest specialtyDTO, CancellationToken ct)
        {
            var existing = await uow.Specialties.GetByNameAsync(specialtyDTO.Name!, ct);
            if (existing is not null)
            {
                logger.LogWarning("Attempted to create duplicate specialty with name {Name}", specialtyDTO.Name);
                return SpecialtyErrors.NameAlreadyExists;
            }

            var specialty = mapper.Map<Specialty>(specialtyDTO);
            await uow.Specialties.CreateAsync(specialty, ct);
            await uow.SaveChangesAsync(ct);

            logger.LogInformation("Specialty {Name} created successfully", specialty.Name);

            return mapper.Map<SpecialtyResponse>(specialty);
        }

        public async Task<Result<List<SpecialtyResponse>>> GetAllAsync(bool includeInactive, CancellationToken ct)
        {
            var specialties = await uow.Specialties.GetAllAsync(ct, includeInactive);
            return mapper.Map<List<SpecialtyResponse>>(specialties);
        }

        public async Task<Result<SpecialtyResponse>> GetByIdAsync(int id, CancellationToken ct)
        {
            var specialty = await uow.Specialties.GetByIdAsync(id, ct);
            if (specialty is null)
            {
                logger.LogWarning("Specialty with id {Id} not found", id);
                return SpecialtyErrors.NotFound(id);
            }

            return mapper.Map<SpecialtyResponse>(specialty);
        }

        public async Task<Result> DeleteByIdAsync(int id, CancellationToken ct)
        {
            var specialty = await uow.Specialties.GetByIdAsync(id, ct);
            if (specialty is null)
            {
                logger.LogWarning("Attempted to delete non-existing specialty with id {Id}", id);
                return SpecialtyErrors.NotFound(id);
            }

            if (await uow.Specialties.HasAssignedDoctorsAsync(id, ct))
            {
                logger.LogWarning("Attempted to delete specialty {Id} with assigned doctors", id);
                return SpecialtyErrors.HasAssignedDoctors;
            }

            uow.Specialties.Delete(specialty);
            await uow.SaveChangesAsync(ct);

            logger.LogInformation("Specialty {Id} deleted successfully", id);
            return Result.Ok();
        }

        public async Task<Result<SpecialtyResponse>> UpdateAsync(int id, SpecialtyRequest specialtyDTO, CancellationToken ct)
        {
            var specialty = await uow.Specialties.GetByIdAsync(id, ct);
            if (specialty is null)
            {
                logger.LogWarning("Attempted to update non-existing specialty with id {Id}", id);
                return SpecialtyErrors.NotFound(id);
            }

            var nameConflict = await uow.Specialties.GetByNameAsync(specialtyDTO.Name!, ct);
            if (nameConflict is not null && nameConflict.Id != id)
            {
                logger.LogWarning("Attempted to rename specialty {Id} to existing name {Name}", id, specialtyDTO.Name);
                return SpecialtyErrors.NameAlreadyExists;
            }

            mapper.Map(specialtyDTO, specialty); // mutate tracked entity
            await uow.SaveChangesAsync(ct);

            logger.LogInformation("Specialty {Id} updated successfully", id);
            return mapper.Map<SpecialtyResponse>(specialty);
        }



    }
}
