using ClinicFlow.Domain.Enums;
using Domain.Interfaces.IRepositories;
using Domain.Parameters;
using Shared.DTOs.Doctor;
using Shared.DTOs.Review;

namespace Services.Implementations
{
    public class DoctorService(IUnitOfWork uow
        , IMapper mapper
        , ILogger<DoctorService> logger) : IDoctorService
    {
        public async Task<Result<IReadOnlyList<DoctorSummaryResponse>>> GetAllAsync(DoctorFilterRequest filter, CancellationToken ct)
        {
            var filterParams = mapper.Map<DoctorFilterParams>(filter);
            var result = await uow.Doctors.GetApprovedPagedAsync(filterParams, ct);
            if (result.Doctors == null || result.TotalCount == 0)
            {
                logger.LogInformation("No doctors found with filter: {@Filter}", filter);
                return Result<IReadOnlyList<DoctorSummaryResponse>>.Ok(new List<DoctorSummaryResponse>()); // returning and empty list
            }

            logger.LogInformation("Retrieved {Count} doctors with filter: {@Filter}", result.TotalCount, filter);// @ is used to log the object as structured data
            return Result<IReadOnlyList<DoctorSummaryResponse>>.Ok(mapper.Map<IReadOnlyList<DoctorSummaryResponse>>(result.Doctors));
        }
        public async Task<Result<DoctorResponse>> GetByIdAsync(int id, CancellationToken ct)
        {
            var doctorProfile = await uow.Doctors.GetByIdAsync(id, ct);
            if (doctorProfile == null)
            {
                logger.LogWarning("Doctor with ID: {Id} not found", id);
                return DoctorErrors.NotFound(id);
            }

            logger.LogInformation("Retrieved doctor with ID: {Id}", id);
            return mapper.Map<DoctorResponse>(doctorProfile);
        }
        public async Task<Result<IEnumerable<ScheduleResponse>>> GetMySchedulesAsync(Guid userId, CancellationToken ct)
        {
            var doctor = await uow.Doctors.GetByUserIdAsync(userId, ct);
            if (doctor is null)
                return DoctorErrors.ProfileNotFound(userId);

            var schedules = await uow.Doctors.GetSchedulesAsync(doctor.Id, ct);
            return Result<IEnumerable<ScheduleResponse>>.Ok( mapper.Map<IEnumerable<ScheduleResponse>>(schedules));
        }
        public async Task<Result> UpdateDoctorProfileAsync(Guid UserId, UpdateDoctorProfileRequest doctorProfile, CancellationToken ct)
        {
            var existingDoc = await uow.Doctors.GetByUserIdAsync(UserId, ct);
            if (existingDoc is null)
            {
                logger.LogWarning("Doctor with ID: {Id} not found for update", UserId);
                return DoctorErrors.ProfileNotFound(UserId);
            }
            mapper.Map(doctorProfile, existingDoc);
            await uow.SaveChangesAsync(ct);
            logger.LogInformation("Updated doctor profile with ID: {Id}", UserId);
            return Result.Ok();
        }
        public async Task<Result<ScheduleResponse>> CreateScheduleAsync(Guid userId, CreateScheduleRequest request, CancellationToken ct)
        {
            var doctor = await uow.Doctors.GetByUserIdAsync(userId, ct);
            if (doctor is null)
            {
                logger.LogWarning("Doctor profile not found for UserId: {UserId}", userId);
                return DoctorErrors.ProfileNotFound(userId);
            }

            var existingSchedule = await uow.Doctors.GetScheduleByUserIdAndDayAsync(userId, request.DayOfWeek, ct);
            if (existingSchedule is not null)
            {
                logger.LogWarning("Schedule for doctor {DoctorId} on {DayOfWeek} already exists",
                    doctor.Id, request.DayOfWeek);
                return DoctorErrors.ScheduleDayAlreadyExists;
            }

            var schedule = mapper.Map<DoctorSchedule>(request);
            schedule.DoctorProfileId = doctor.Id; // from the doctor we fetched
            schedule.DayOfWeek = request.DayOfWeek;

            uow.Doctors.AddSchedule(schedule);
            await uow.SaveChangesAsync(ct);

            await GenerateSlotsAsync(schedule, ct);

            logger.LogInformation("Created schedule for doctor {DoctorId} on {DayOfWeek}", doctor.Id, request.DayOfWeek);
            return mapper.Map<ScheduleResponse>(schedule);
        }
        public async Task<Result<ScheduleResponse>> UpdateScheduleAsync(int scheduleId, Guid userId, UpdateScheduleRequest request, CancellationToken ct)
        {
            var existing = await uow.Doctors.GetScheduleByIdAsync(scheduleId, ct);
            if(existing is null || existing.DoctorProfile.UserId != userId)
            {
                logger.LogWarning("Schedule with ID: {ScheduleId} not found for User ID: {UserId}", scheduleId, userId);
                return DoctorErrors.ScheduleNotFound(scheduleId);
            }
            var updatedSchedule = mapper.Map(request, existing);
            await uow.SaveChangesAsync(ct);
            logger.LogInformation("Updated schedule with ID: {ScheduleId} for doctor ID: {UserId}", scheduleId, userId);

            await uow.Slots.DeleteFutureAvailableByScheduleAsync(scheduleId, ct);
            await GenerateSlotsAsync(updatedSchedule, ct);
            logger.LogInformation("Regenerated slots for updated schedule ID: {ScheduleId}", scheduleId);

            return mapper.Map<ScheduleResponse>(updatedSchedule);
        }
        public async Task<Result> DeleteScheduleAsync(int scheduleId, Guid userId, CancellationToken ct)
        {
            var existing = await uow.Doctors.GetScheduleByIdAsync(scheduleId, ct);
            if (existing is null || existing.DoctorProfile.UserId != userId)
            {
                logger.LogWarning("Schedule with ID: {ScheduleId} not found for doctor ID: {userId}", scheduleId, userId);
                return DoctorErrors.ScheduleNotFound(scheduleId);
            }
            uow.Doctors.DeleteSchedule(existing);
            await uow.SaveChangesAsync(ct);
            logger.LogInformation("Deleted schedule with ID: {ScheduleId} for doctor ID: {userId}", scheduleId, userId);
            return Result.Ok();

        }
        public async Task<Result<IEnumerable<ReviewResponse>>> GetReviewsAsync(int id,ReviewFilterRequest request, CancellationToken ct)
        {
            var reviewFilterParams = mapper.Map<ReviewFilterParams>(request);
            var reviews = await uow.Reviews.GetByDoctorIdAsync(id,reviewFilterParams, ct);
            logger.LogInformation("Retrieved reviews for doctor ID: {DoctorId}", id);
            return Result<IEnumerable<ReviewResponse>>.Ok(mapper.Map<IEnumerable<ReviewResponse>>(reviews.Reviews));
        }
        public async Task<Result<List<SlotResponse>>> GetSlotsByDateAsync(int doctorId, DateOnly date, CancellationToken ct)
        {
            var existing =await uow.Slots.GetAvailableByDoctorAndDateAsync(doctorId, date, ct);
            if(existing is null)
            {
                logger.LogWarning("No schedule found for doctor ID: {DoctorId} on date: {Date}", doctorId, date);
                return DoctorErrors.ScheduleNotFoundForDate(doctorId, date);
            }

            logger.LogInformation("Retrieved slots for doctor ID: {DoctorId} on date: {Date}", doctorId, date);
            return mapper.Map<List<SlotResponse>>(existing);
        }

        #region Helpers

        private async Task GenerateSlotsAsync(DoctorSchedule schedule, CancellationToken ct)
        {
            var slots = new List<AppointmentSlot>();
            var Today = DateOnly.FromDateTime(DateTime.UtcNow);

            for (int DayOfSet = 0; DayOfSet < 14; DayOfSet++)
            {
                var date = Today.AddDays(DayOfSet);

                // Only generate for the matching day of week
                if (date.DayOfWeek != schedule.DayOfWeek) continue;

                var currentTime = schedule.StartTime;
                while (currentTime < schedule.EndTime)
                {
                    slots.Add(new AppointmentSlot
                    {
                        StartTime = currentTime,
                        EndTime = currentTime.AddMinutes(schedule.SlotDurationMinutes),
                        DoctorScheduleId = schedule.Id,
                        DoctorProfileId = schedule.DoctorProfileId,
                        Status = SlotStatus.Available,
                        Date = date
                        
                    });
                    currentTime = currentTime.AddMinutes(schedule.SlotDurationMinutes);
                }
            }
            await uow.Slots.AddRangeAsync(slots, ct);
            await uow.SaveChangesAsync(ct);
        }

        #endregion

    }
}
