using ClinicFlow.Domain.Enums;
using Domain.Interfaces.IRepositories;
using Domain.Parameters;
using Domain.Time;
using Shared.DTOs.Doctor;
using Shared.DTOs.Review;

namespace Services.Implementations
{
    public class DoctorService(IUnitOfWork uow
        , IMapper mapper
        , ILogger<DoctorService> logger
        , IClock clock
        , ITenantContext tenant) : IDoctorService
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
            // Unapproved and suspended doctors are hidden from the public profile (REVIEW.md M1)
            var doctorProfile = await uow.Doctors.GetDetailedByIdAsync(id, ct);
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
            if (existingSchedule is { IsActive: true })
            {
                logger.LogWarning("Schedule for doctor {DoctorId} on {DayOfWeek} already exists",
                    doctor.Id, request.DayOfWeek);
                return DoctorErrors.ScheduleDayAlreadyExists;
            }

            // The schedule must start and end while its branch is open that day (FR-ORG-03); a reused one keeps its branch
            var calendar = await uow.Branches.GetCalendarAsync(existingSchedule?.BranchId ?? tenant.DefaultBranchId, ct);
            if (!calendar.CoversSchedule(request.DayOfWeek, request.StartTime, request.EndTime))
            {
                logger.LogWarning("Schedule for doctor {DoctorId} on {DayOfWeek} rejected: {StartTime}-{EndTime} is outside the branch's hours",
                    doctor.Id, request.DayOfWeek, request.StartTime, request.EndTime);
                return DoctorErrors.ScheduleOutsideBranchHours;
            }

            // An inactive schedule for this day (e.g. one kept for its appointment history when it was
            // deleted) is reused, since there is one schedule per weekday (REVIEW.md H4)
            if (existingSchedule is not null)
            {
                mapper.Map(request, existingSchedule);
                existingSchedule.IsActive = true;
                await uow.SaveChangesAsync(ct);
                var zone = await uow.Branches.GetTimeZoneIdAsync(existingSchedule.BranchId, ct);
                await uow.Slots.SyncKeptSlotsWithScheduleAsync(existingSchedule, clock.Today(zone), ct);
                await GenerateSlotsAsync(existingSchedule, zone, calendar, ct);

                logger.LogInformation("Reactivated schedule {ScheduleId} for doctor {DoctorId} on {DayOfWeek}",
                    existingSchedule.Id, doctor.Id, request.DayOfWeek);
                return mapper.Map<ScheduleResponse>(existingSchedule);
            }

            var schedule = mapper.Map<DoctorSchedule>(request);
            schedule.DoctorProfileId = doctor.Id; // from the doctor we fetched
            schedule.DayOfWeek = request.DayOfWeek;
            schedule.BranchId = tenant.DefaultBranchId; // the only branch until P1 lets staff choose

            uow.Doctors.AddSchedule(schedule);
            await uow.SaveChangesAsync(ct);

            await GenerateSlotsAsync(schedule, await uow.Branches.GetTimeZoneIdAsync(schedule.BranchId, ct), calendar, ct);

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

            // The validator only compares the times when both are sent; check the merged result
            if (updatedSchedule.EndTime <= updatedSchedule.StartTime
                || (updatedSchedule.EndTime - updatedSchedule.StartTime).TotalMinutes < updatedSchedule.SlotDurationMinutes)
            {
                logger.LogWarning("Schedule {ScheduleId} update rejected: {StartTime}-{EndTime} cannot fit a {Duration}-minute slot",
                    scheduleId, updatedSchedule.StartTime, updatedSchedule.EndTime, updatedSchedule.SlotDurationMinutes);
                return DoctorErrors.InvalidScheduleTime;
            }

            var calendar = await uow.Branches.GetCalendarAsync(updatedSchedule.BranchId, ct);
            if (updatedSchedule.IsActive
                && !calendar.CoversSchedule(updatedSchedule.DayOfWeek, updatedSchedule.StartTime, updatedSchedule.EndTime))
            {
                logger.LogWarning("Schedule {ScheduleId} update rejected: {StartTime}-{EndTime} is outside the branch's hours",
                    scheduleId, updatedSchedule.StartTime, updatedSchedule.EndTime);
                return DoctorErrors.ScheduleOutsideBranchHours;
            }

            // One transaction, so a failure can't leave the schedule changed but its slots half-replaced (REVIEW.md H4).
            // Booked slots, and slots an appointment still points to, are kept; regeneration skips their times.
            // Kept free slots outside the new hours are blocked so patients can't book them.
            var zone = await uow.Branches.GetTimeZoneIdAsync(updatedSchedule.BranchId, ct);
            var created = await uow.ExecuteInTransactionAsync(async transaction =>
            {
                var today = clock.Today(zone);
                await uow.SaveChangesAsync(ct);
                await uow.Slots.DeleteFutureAvailableByScheduleAsync(scheduleId, today, ct);
                await uow.Slots.SyncKeptSlotsWithScheduleAsync(updatedSchedule, today, ct);
                var slotCount = await GenerateSlotsAsync(updatedSchedule, zone, calendar, ct);
                await transaction.CommitAsync(ct);
                return slotCount;
            }, ct);

            logger.LogInformation("Updated schedule {ScheduleId} for doctor {UserId} and regenerated {SlotCount} free slots", scheduleId, userId, created);
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

            // Slot dates are in the schedule's branch zone, so "today" is that branch's date (docs/v2/adr/0003)
            var today = clock.Today(await uow.Branches.GetTimeZoneIdAsync(existing.BranchId, ct));

            // Upcoming appointments would be orphaned: the doctor cancels them first (REVIEW.md H4)
            if (await uow.Slots.HasUpcomingBookingsAsync(scheduleId, today, ct))
            {
                logger.LogWarning("Schedule {ScheduleId} has upcoming booked appointments and can't be deleted", scheduleId);
                return DoctorErrors.CannotDeleteScheduleWithBookedSlots;
            }

            var deactivated = await uow.ExecuteInTransactionAsync(async transaction =>
            {
                await uow.Slots.DeleteUnusedByScheduleAsync(scheduleId, ct);

                // Slots that remain belong to past or cancelled appointments; the appointment→slot FK is NoAction,
                // so the schedule is deactivated instead of deleted to keep that history
                var hasHistory = await uow.Slots.AnyForScheduleAsync(scheduleId, ct);
                if (hasHistory)
                {
                    existing.IsActive = false;
                    // Future free slots of cancelled appointments stay, but are no longer bookable
                    await uow.Slots.SyncKeptSlotsWithScheduleAsync(existing, today, ct);
                }
                else
                {
                    uow.Doctors.DeleteSchedule(existing);
                }
                await uow.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return hasHistory;
            }, ct);

            if (deactivated)
                logger.LogInformation("Deactivated schedule {ScheduleId} for doctor {UserId}: it has appointment history", scheduleId, userId);
            else
                logger.LogInformation("Deleted schedule {ScheduleId} for doctor {UserId}", scheduleId, userId);
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
            // An unknown doctor or a day off is an empty list, not an error (contract #11)
            var slots = await uow.Slots.GetAvailableByDoctorAndDateAsync(doctorId, date, ct);

            logger.LogInformation("Retrieved slots for doctor ID: {DoctorId} on date: {Date}", doctorId, date);
            return mapper.Map<List<SlotResponse>>(slots);
        }

        public async Task<int> GenerateUpcomingSlotsAsync(CancellationToken ct)
        {
            var schedules = await uow.Doctors.GetActiveSchedulesAsync(ct);
            var zones = await uow.Branches.GetTimeZoneIdsAsync(ct);
            var calendars = await uow.Branches.GetCalendarsAsync(ct);
            var created = 0;

            foreach (var schedule in schedules)
            {
                try
                {
                    created += await GenerateSlotsAsync(schedule, zones[schedule.BranchId], calendars[schedule.BranchId], ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Most likely a doctor editing this schedule at the same moment (unique index on doctor, date, start).
                    // The failed slots stay in the change tracker and would fail every later save, so stop here;
                    // generation is idempotent and the next run fills the gap
                    logger.LogError(ex, "Slot generation failed for schedule {ScheduleId}; the remaining schedules wait for the next run", schedule.Id);
                    break;
                }
            }

            logger.LogInformation("Generated {Count} slots for {ScheduleCount} active schedules", created, schedules.Count);
            return created;
        }

        #region Helpers

        // Slots are published this many days ahead, counting today; SlotGenerationJob rolls the window forward daily (REVIEW.md H8)
        private const int SlotWindowDays = 14;

        // timeZoneId is the schedule's branch zone: slot dates and times are that branch's wall clock (docs/v2/adr/0003)
        // and calendar its hours and holidays: no slot on a holiday, or outside an opening period (FR-ORG-03)
        private async Task<int> GenerateSlotsAsync(DoctorSchedule schedule, string timeZoneId, BranchCalendar calendar, CancellationToken ct)
        {
            // Inactive schedules publish no bookable slots
            if (!schedule.IsActive || schedule.SlotDurationMinutes <= 0)
                return 0;

            var slots = new List<AppointmentSlot>();
            var today = clock.Today(timeZoneId);

            // Idempotent: a time that overlaps an existing slot (e.g. a booked one kept through an update) is skipped,
            // instead of violating the unique index on (doctor, date, start) or double-booking the doctor (REVIEW.md H4)
            var existing = await uow.Slots.GetSlotTimesAsync(schedule.DoctorProfileId, today, today.AddDays(SlotWindowDays - 1), ct);

            // Work in minutes since midnight: TimeOnly.AddMinutes wraps at midnight, which made
            // a late-evening schedule loop forever. Only whole slots that end by EndTime are created.
            var duration = schedule.SlotDurationMinutes;
            var startMinutes = (int)schedule.StartTime.ToTimeSpan().TotalMinutes;
            var endMinutes = (int)schedule.EndTime.ToTimeSpan().TotalMinutes;

            for (int dayOffset = 0; dayOffset < SlotWindowDays; dayOffset++)
            {
                var date = today.AddDays(dayOffset);

                // Only generate for the matching day of week, when it isn't a holiday
                if (date.DayOfWeek != schedule.DayOfWeek || calendar.IsHoliday(date)) continue;

                for (var minute = startMinutes; minute + duration <= endMinutes; minute += duration)
                {
                    var slotStart = TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(minute));
                    var slotEnd = slotStart.AddMinutes(duration);
                    if (existing.Any(e => e.Date == date && e.StartTime < slotEnd && slotStart < e.EndTime)) continue;
                    if (!calendar.IsOpen(date, slotStart, slotEnd)) continue; // e.g. a slot that runs past closing time

                    // A time daylight saving skips doesn't exist, so neither does a slot touching it; a repeated time
                    // is its first occurrence (Domain.Time.WallClock)
                    if (clock.ToUtc(timeZoneId, date, slotStart) is not { } startsAtUtc
                        || clock.ToUtc(timeZoneId, date, slotEnd) is not { } endsAtUtc)
                        continue;

                    slots.Add(new AppointmentSlot
                    {
                        StartTime = slotStart,
                        EndTime = slotEnd,
                        StartsAtUtc = startsAtUtc,
                        EndsAtUtc = endsAtUtc,
                        DoctorScheduleId = schedule.Id,
                        BranchId = schedule.BranchId,
                        DoctorProfileId = schedule.DoctorProfileId,
                        Status = SlotStatus.Available,
                        Date = date
                    });
                }
            }
            await uow.Slots.AddRangeAsync(slots, ct);
            await uow.SaveChangesAsync(ct);
            return slots.Count;
        }

        #endregion

    }
}
