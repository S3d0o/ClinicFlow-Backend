using Domain.Entities.AppModule;
using Shared.DTOs.Doctor;
using Shared.DTOs.Review;

namespace Services.Abstraction.Contracts
{
    public interface IDoctorService
    {
        public Task<Result<IReadOnlyList<DoctorSummaryResponse>>> GetAllAsync(DoctorFilterRequest filter, CancellationToken ct);
        public Task<Result<DoctorResponse>> GetByIdAsync(int id, CancellationToken ct);
        public Task<Result<List<SlotResponse>>> GetSlotsByDateAsync(int doctorId, DateOnly date, CancellationToken ct);
        public Task<Result<IEnumerable<ReviewResponse>>> GetReviewsAsync(int id, ReviewFilterRequest request, CancellationToken ct);
        public Task<Result> UpdateDoctorProfileAsync(Guid UserId, UpdateDoctorProfileRequest doctorProfile, CancellationToken ct);
        public Task<Result<IEnumerable<ScheduleResponse>>> GetMySchedulesAsync(Guid userId, CancellationToken ct);
        public Task<Result<ScheduleResponse>> CreateScheduleAsync(Guid UserId, CreateScheduleRequest request, CancellationToken ct);
        public Task<Result<ScheduleResponse>> UpdateScheduleAsync(int scheduleId, Guid UserId, UpdateScheduleRequest request, CancellationToken ct);
        public Task<Result> DeleteScheduleAsync(int scheduleId, Guid UserId, CancellationToken ct);

    }
}
