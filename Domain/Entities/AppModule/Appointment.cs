using ClinicFlow.Domain.Enums;

namespace Domain.Entities.AppModule
{
    // Owns the appointment status rules (docs/v2/adr/0004, 05 §4.1). The status and what each transition records
    // only change through the methods below; services load the appointment, call one and save
    public class Appointment : BaseEntity<int>, IOrganizationOwned, IBranchOwned, IHasDomainEvents
    {
        // The patient may be late: a no-show can only be marked this long after the start (v2 FR-APT-07)
        public static readonly TimeSpan NoShowGracePeriod = TimeSpan.FromMinutes(15);

        // Patients can't cancel or reschedule closer to the start than this
        public static readonly TimeSpan PatientChangeWindow = TimeSpan.FromHours(2);

        public int OrganizationId { get; set; }
        public int BranchId { get; set; }

        public int SlotId { get; set; }
        public int PatientProfileId { get; set; }
        public int DoctorProfileId { get; set; } // denormalized — avoids joining through Slot every query

        public AppointmentStatus Status { get; private set; } = AppointmentStatus.Pending;

        public string? ReasonForVisit { get; set; }  // patient sets this at booking time
        public string? DoctorNotes { get; private set; } // only the owning doctor can write this

        public DateTime BookedAt { get; set; }

        // All nullable — only populated when the appointment is cancelled
        public DateTime? CancelledAt { get; private set; }
        public string? CancellationReason { get; private set; }
        public CancelledBy? CancelledBy { get; private set; }

        // When each later step happened; null until it does
        public DateTime? CheckedInAt { get; private set; }
        public DateTime? StartedAt { get; private set; }
        public DateTime? CompletedAt { get; private set; }
        public DateTime? NoShowAt { get; private set; }

        // Set on the new appointment of a reschedule; the old one is Rescheduled
        public int? RescheduledFromId { get; private set; }

        // Set by QueueReminder when the reminder job hands the reminder to the outbox (the demo seeder sets it directly)
        public DateTime? ReminderSentAt { get; set; }

        // Navigation
        public AppointmentSlot Slot { get; set; } = null!;
        public PatientProfile Patient { get; set; } = null!;
        public DoctorProfile Doctor { get; set; } = null!;
        public Review? Review { get; set; } // null until patient submits one
        public Appointment? RescheduledFrom { get; private set; }
        public Appointment? RescheduledTo { get; private set; }

        // A confirmed appointment for an available slot. Whether the slot can be booked (its status, the past,
        // the doctor) is checked by the caller, which also marks the slot booked
        public static Appointment Book(AppointmentSlot slot, int patientProfileId, string? reasonForVisit, DateTime utcNow) => new()
        {
            SlotId = slot.Id,
            BranchId = slot.BranchId,
            DoctorProfileId = slot.DoctorProfileId,
            PatientProfileId = patientProfileId,
            ReasonForVisit = reasonForVisit,
            Status = AppointmentStatus.Confirmed,
            BookedAt = utcNow,
        };

        // ── Transitions (05 §4.1). Each needs Slot loaded when it has a time guard ──

        // Confirmed → CheckedIn, on the day of the visit in the branch's zone
        public Result CheckIn(DateTime utcNow, DateOnly branchToday)
        {
            if (Status != AppointmentStatus.Confirmed)
                return StatusError(AppointmentStatus.CheckedIn);
            if (Slot.Date != branchToday)
                return AppointmentErrors.CheckInNotToday;

            Status = AppointmentStatus.CheckedIn;
            CheckedInAt = utcNow;
            Raise(new PatientCheckedIn(Id));
            return Result.Ok();
        }

        // CheckedIn → InProgress: the doctor calls the patient in. That the doctor has no other visit in progress
        // needs a query, so the caller checks it
        public Result Start(DateTime utcNow)
        {
            if (Status != AppointmentStatus.CheckedIn)
                return StatusError(AppointmentStatus.InProgress);

            Status = AppointmentStatus.InProgress;
            StartedAt = utcNow;
            Raise(new AppointmentStarted(Id));
            return Result.Ok();
        }

        // InProgress → Completed. Confirmed → Completed is also allowed, once the visit's start has passed: the
        // inherited complete route has no check-in or start before it (ADR 0004 amendment)
        public Result Complete(DateTime utcNow)
        {
            if (Status == AppointmentStatus.Cancelled)
                return AppointmentErrors.CannotCompleteCancelled;
            if (Status is not (AppointmentStatus.InProgress or AppointmentStatus.Confirmed))
                return StatusError(AppointmentStatus.Completed);
            // Completing a visit nobody started would unlock a review for a visit that never took place (REVIEW.md M14)
            if (Status == AppointmentStatus.Confirmed && utcNow < Slot.StartsAtUtc)
                return AppointmentErrors.CannotCompleteBeforeStart;

            Status = AppointmentStatus.Completed;
            CompletedAt = utcNow;
            Raise(new AppointmentCompleted(Id));
            return Result.Ok();
        }

        // Pending, Confirmed or CheckedIn (left without being seen) → Cancelled. A patient can only cancel
        // until 2 hours before the start; doctors and admins at any time. The caller frees the slot
        public Result Cancel(CancelledBy by, DateTime utcNow, string? reason)
        {
            if (Status is not (AppointmentStatus.Pending or AppointmentStatus.Confirmed or AppointmentStatus.CheckedIn))
                return StatusError(AppointmentStatus.Cancelled);
            if (by == ClinicFlow.Domain.Enums.CancelledBy.Patient && !IsBeforePatientChangeWindow(utcNow))
                return AppointmentErrors.CancellationWindowExpired;

            Status = AppointmentStatus.Cancelled;
            CancelledAt = utcNow;
            CancelledBy = by;
            CancellationReason = reason;
            Raise(new AppointmentCancelled(Id, by));
            return Result.Ok();
        }

        // Confirmed → NoShow, once the grace period after the start has passed. Staff mark it, never a job (FR-APT-07)
        public Result MarkNoShow(DateTime utcNow)
        {
            if (Status != AppointmentStatus.Confirmed)
                return StatusError(AppointmentStatus.NoShow);
            if (utcNow < Slot.StartsAtUtc + NoShowGracePeriod)
                return AppointmentErrors.NoShowTooEarly;

            Status = AppointmentStatus.NoShow;
            NoShowAt = utcNow;
            Raise(new AppointmentMarkedNoShow(Id));
            return Result.Ok();
        }

        // NoShow → Confirmed: a correction on the day of the visit in the branch's zone. Who may do it, and the
        // audit entry, are the caller's (05 §4.1)
        public Result RevertNoShow(DateOnly branchToday)
        {
            if (Status != AppointmentStatus.NoShow)
                return StatusError(AppointmentStatus.Confirmed);
            if (Slot.Date != branchToday)
                return AppointmentErrors.NoShowCorrectionExpired;

            Status = AppointmentStatus.Confirmed;
            NoShowAt = null;
            Raise(new AppointmentNoShowReverted(Id));
            return Result.Ok();
        }

        // Confirmed → Rescheduled, linked to its replacement: a new confirmed appointment for the same patient,
        // created in the same transaction. `by` decides the patient window, as for a cancellation
        public Result RescheduleTo(Appointment replacement, CancelledBy by, DateTime utcNow)
        {
            if (ReferenceEquals(replacement, this) || replacement.Status != AppointmentStatus.Confirmed
                || replacement.PatientProfileId != PatientProfileId
                || replacement.RescheduledFrom is not null || replacement.RescheduledFromId is not null)
                throw new ArgumentException("The replacement must be another new confirmed appointment for the same patient", nameof(replacement));

            if (Status != AppointmentStatus.Confirmed)
                return StatusError(AppointmentStatus.Rescheduled);
            if (by == ClinicFlow.Domain.Enums.CancelledBy.Patient && !IsBeforePatientChangeWindow(utcNow))
                return AppointmentErrors.CancellationWindowExpired;

            Status = AppointmentStatus.Rescheduled;
            RescheduledTo = replacement;
            replacement.RescheduledFrom = this;
            Raise(new AppointmentRescheduled(Id));
            return Result.Ok();
        }

        // Notes can't be added to an appointment that never took place in this record (05 §4.1; moves to Encounter in P4)
        public Result AddDoctorNotes(string? notes)
        {
            if (Status is AppointmentStatus.Cancelled or AppointmentStatus.Pending or AppointmentStatus.Rescheduled)
                return AppointmentErrors.DoctorNotesNotAllowed;

            DoctorNotes = notes;
            return Result.Ok();
        }

        // The reminder job marks the visit as reminded; the email and the notification go out through the outbox
        public void QueueReminder(DateTime utcNow)
        {
            ReminderSentAt = utcNow;
            Raise(new AppointmentReminderDue(Id));
        }

        // ── Domain events (06 §5): a field, not a property, so EF doesn't map it ──

        private readonly List<IDomainEvent> _domainEvents = [];

        private void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

        public IReadOnlyList<IDomainEvent> TakeDomainEvents()
        {
            var taken = _domainEvents.ToList();
            _domainEvents.Clear();
            return taken;
        }

        private bool IsBeforePatientChangeWindow(DateTime utcNow) => utcNow <= Slot.StartsAtUtc - PatientChangeWindow;

        // Why the current status can't move to `target`. The terminal states keep the error codes the API had
        // before the state machine (contract 3.x); every other illegal move is InvalidStatusTransition
        private Error StatusError(AppointmentStatus target) => Status switch
        {
            AppointmentStatus.Cancelled => AppointmentErrors.AlreadyCancelled,
            AppointmentStatus.Completed => AppointmentErrors.AlreadyCompleted,
            AppointmentStatus.NoShow => AppointmentErrors.AlreadyNoShow,
            _ => AppointmentErrors.InvalidStatusTransition(Status, target),
        };
    }
}
