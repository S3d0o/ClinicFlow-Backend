
using Shared.DTOs.Specialty;

namespace Services.Abstraction.Contracts
{
    public interface ISpecialtyService
    {
        public Task<Result<List<SpecialtyResponse>>> GetAllAsync(bool includeInactive, CancellationToken ct);
        public Task<Result<SpecialtyResponse>> GetByIdAsync(int id, CancellationToken ct);

        public Task<Result<SpecialtyResponse>> CreateAsync(SpecialtyRequest specialtyDTO, CancellationToken ct);
        public Task<Result<SpecialtyResponse>> UpdateAsync(int id, SpecialtyRequest specialtyDTO, CancellationToken ct);
        public Task<Result> DeleteByIdAsync(int id, CancellationToken ct);

    }
}
