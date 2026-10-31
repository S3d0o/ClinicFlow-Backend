using ClinicFlow.Domain.Enums;
using Domain.Entities.AppModule;
using Domain.Enums;
using Domain.ResultPattern;
using static ClinicFlow.Tests.Domain.Appointments.TestAppointments;

namespace ClinicFlow.Tests.Domain.Appointments
{
    // Every status × every transition (docs/v2/adr/0004, 05 §4.1), with each time guard satisfied, so only the
    // status decides. The terminal states keep the codes the API already returned; other illegal moves are
    // Appointment.InvalidStatusTransition
    public class AppointmentTransitionTableTests
    {
        private static readonly Dictionary<string, Func<Appointment, Result>> Transitions = new()
        {
            ["CheckIn"] = a => a.CheckIn(Start.AddMinutes(-20), VisitDay),
            ["Start"] = a => a.Start(Start),
            ["Complete"] = a => a.Complete(Start.AddMinutes(30)),
            ["Cancel"] = a => a.Cancel(CancelledBy.Admin, Start.AddMinutes(5), null, Window),
            ["MarkNoShow"] = a => a.MarkNoShow(Start.AddMinutes(20), Grace),
            ["RevertNoShow"] = a => a.RevertNoShow(VisitDay),
            ["RescheduleTo"] = a => a.RescheduleTo(Booked(), CancelledBy.Admin, Start.AddDays(-1), Window),
        };

        private const string Invalid = "Appointment.InvalidStatusTransition";
        private const string AlreadyCancelled = "Appointment.AlreadyCancelled";
        private const string AlreadyCompleted = "Appointment.AlreadyCompleted";
        private const string AlreadyNoShow = "Appointment.AlreadyNoShow";

        // (from, transition) → the new status, or the error code
        private static readonly Dictionary<(AppointmentStatus From, string Transition), object> Expected = new()
        {
            [(AppointmentStatus.Pending, "CheckIn")] = Invalid,
            [(AppointmentStatus.Pending, "Start")] = Invalid,
            [(AppointmentStatus.Pending, "Complete")] = Invalid,
            [(AppointmentStatus.Pending, "Cancel")] = AppointmentStatus.Cancelled,
            [(AppointmentStatus.Pending, "MarkNoShow")] = Invalid,
            [(AppointmentStatus.Pending, "RevertNoShow")] = Invalid,
            [(AppointmentStatus.Pending, "RescheduleTo")] = Invalid,

            [(AppointmentStatus.Confirmed, "CheckIn")] = AppointmentStatus.CheckedIn,
            [(AppointmentStatus.Confirmed, "Start")] = Invalid,
            [(AppointmentStatus.Confirmed, "Complete")] = AppointmentStatus.Completed, // the inherited complete route
            [(AppointmentStatus.Confirmed, "Cancel")] = AppointmentStatus.Cancelled,
            [(AppointmentStatus.Confirmed, "MarkNoShow")] = AppointmentStatus.NoShow,
            [(AppointmentStatus.Confirmed, "RevertNoShow")] = Invalid,
            [(AppointmentStatus.Confirmed, "RescheduleTo")] = AppointmentStatus.Rescheduled,

            [(AppointmentStatus.CheckedIn, "CheckIn")] = Invalid,
            [(AppointmentStatus.CheckedIn, "Start")] = AppointmentStatus.InProgress,
            [(AppointmentStatus.CheckedIn, "Complete")] = Invalid,
            [(AppointmentStatus.CheckedIn, "Cancel")] = AppointmentStatus.Cancelled, // left without being seen
            [(AppointmentStatus.CheckedIn, "MarkNoShow")] = Invalid,
            [(AppointmentStatus.CheckedIn, "RevertNoShow")] = Invalid,
            [(AppointmentStatus.CheckedIn, "RescheduleTo")] = Invalid,

            [(AppointmentStatus.InProgress, "CheckIn")] = Invalid,
            [(AppointmentStatus.InProgress, "Start")] = Invalid,
            [(AppointmentStatus.InProgress, "Complete")] = AppointmentStatus.Completed,
            [(AppointmentStatus.InProgress, "Cancel")] = Invalid,
            [(AppointmentStatus.InProgress, "MarkNoShow")] = Invalid,
            [(AppointmentStatus.InProgress, "RevertNoShow")] = Invalid,
            [(AppointmentStatus.InProgress, "RescheduleTo")] = Invalid,

            [(AppointmentStatus.Completed, "CheckIn")] = AlreadyCompleted,
            [(AppointmentStatus.Completed, "Start")] = AlreadyCompleted,
            [(AppointmentStatus.Completed, "Complete")] = AlreadyCompleted,
            [(AppointmentStatus.Completed, "Cancel")] = AlreadyCompleted,
            [(AppointmentStatus.Completed, "MarkNoShow")] = AlreadyCompleted,
            [(AppointmentStatus.Completed, "RevertNoShow")] = AlreadyCompleted,
            [(AppointmentStatus.Completed, "RescheduleTo")] = AlreadyCompleted,

            [(AppointmentStatus.Cancelled, "CheckIn")] = AlreadyCancelled,
            [(AppointmentStatus.Cancelled, "Start")] = AlreadyCancelled,
            [(AppointmentStatus.Cancelled, "Complete")] = "Appointment.CannotCompleteCancelled",
            [(AppointmentStatus.Cancelled, "Cancel")] = AlreadyCancelled,
            [(AppointmentStatus.Cancelled, "MarkNoShow")] = AlreadyCancelled,
            [(AppointmentStatus.Cancelled, "RevertNoShow")] = AlreadyCancelled,
            [(AppointmentStatus.Cancelled, "RescheduleTo")] = AlreadyCancelled,

            [(AppointmentStatus.NoShow, "CheckIn")] = AlreadyNoShow,
            [(AppointmentStatus.NoShow, "Start")] = AlreadyNoShow,
            [(AppointmentStatus.NoShow, "Complete")] = AlreadyNoShow,
            [(AppointmentStatus.NoShow, "Cancel")] = AlreadyNoShow,
            [(AppointmentStatus.NoShow, "MarkNoShow")] = AlreadyNoShow,
            [(AppointmentStatus.NoShow, "RevertNoShow")] = AppointmentStatus.Confirmed, // same-day correction
            [(AppointmentStatus.NoShow, "RescheduleTo")] = AlreadyNoShow,

            [(AppointmentStatus.Rescheduled, "CheckIn")] = Invalid,
            [(AppointmentStatus.Rescheduled, "Start")] = Invalid,
            [(AppointmentStatus.Rescheduled, "Complete")] = Invalid,
            [(AppointmentStatus.Rescheduled, "Cancel")] = Invalid,
            [(AppointmentStatus.Rescheduled, "MarkNoShow")] = Invalid,
            [(AppointmentStatus.Rescheduled, "RevertNoShow")] = Invalid,
            [(AppointmentStatus.Rescheduled, "RescheduleTo")] = Invalid,
        };

        public static TheoryData<AppointmentStatus, string> Cases()
        {
            var cases = new TheoryData<AppointmentStatus, string>();
            foreach (var (from, transition) in Expected.Keys)
                cases.Add(from, transition);
            return cases;
        }

        [Fact]
        public void Table_CoversEveryStatusAndTransition()
        {
            var all = Enum.GetValues<AppointmentStatus>().SelectMany(s => Transitions.Keys.Select(t => (s, t)));

            Assert.Equal(all.Order(), Expected.Keys.Select(k => (k.From, k.Transition)).Order());
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void Transition_FromStatus_MovesOrFailsAsTheTableSays(AppointmentStatus from, string transition)
        {
            var appointment = In(from);

            var result = Transitions[transition](appointment);

            if (Expected[(from, transition)] is AppointmentStatus to)
            {
                Assert.True(result.IsSuccess, string.Join(", ", result.Errors.Select(e => e.Code)));
                Assert.Equal(to, appointment.Status);
            }
            else
            {
                var error = Assert.Single(result.Errors);
                Assert.Equal((string)Expected[(from, transition)], error.Code);
                Assert.Equal(ErrorType.Conflict, error.Type); // every status error is a 409
                Assert.Equal(from, appointment.Status);       // a refused transition changes nothing
            }
        }

        [Fact]
        public void StateProperties_HaveNoPublicSetter()
        {
            // Services can't set them; only the transitions do (ADR 0004 item 3)
            string[] owned =
            [
                nameof(Appointment.Status), nameof(Appointment.DoctorNotes), nameof(Appointment.CancelledAt),
                nameof(Appointment.CancellationReason), nameof(Appointment.CancelledBy), nameof(Appointment.CheckedInAt),
                nameof(Appointment.StartedAt), nameof(Appointment.CompletedAt), nameof(Appointment.NoShowAt),
                nameof(Appointment.RescheduledFromId), nameof(Appointment.RescheduledFrom), nameof(Appointment.RescheduledTo),
            ];

            var settable = owned.Where(p => typeof(Appointment).GetProperty(p)!.SetMethod?.IsPublic == true);

            Assert.Empty(settable);
        }
    }
}
