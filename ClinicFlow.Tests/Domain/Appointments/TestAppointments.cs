using ClinicFlow.Domain.Enums;
using Domain.Entities.AppModule;
using Domain.Enums;
using Domain.ResultPattern;

namespace ClinicFlow.Tests.Domain.Appointments
{
    // A 10:00–10:30 visit in Cairo on Fri 23 Oct 2026 (UTC+3), booked two days before. Every status is reached
    // through the transitions, so the tests also show that each one is reachable
    internal static class TestAppointments
    {
        public const int PatientProfileId = 7;
        public static readonly DateOnly VisitDay = new(2026, 10, 23);
        public static readonly DateTime Start = new(2026, 10, 23, 7, 0, 0, DateTimeKind.Utc);

        public static AppointmentSlot Slot() => new()
        {
            Id = 5,
            BranchId = 2,
            DoctorProfileId = 3,
            Date = VisitDay,
            StartTime = new TimeOnly(10, 0),
            EndTime = new TimeOnly(10, 30),
            StartsAtUtc = Start,
            EndsAtUtc = Start.AddMinutes(30),
        };

        public static Appointment Booked()
        {
            var slot = Slot();
            var appointment = Appointment.Book(slot, PatientProfileId, "Check-up", Start.AddDays(-2));
            appointment.Slot = slot;
            return appointment;
        }

        public static Appointment In(AppointmentStatus status)
        {
            // Nothing books into Pending any more (05 §4.1); it is the status of an unsaved appointment and of V1 data
            if (status == AppointmentStatus.Pending)
                return new Appointment { Slot = Slot(), PatientProfileId = PatientProfileId };

            var appointment = Booked();
            switch (status)
            {
                case AppointmentStatus.Confirmed:
                    break;
                case AppointmentStatus.CheckedIn:
                    Must(appointment.CheckIn(Start.AddMinutes(-20), VisitDay));
                    break;
                case AppointmentStatus.InProgress:
                    Must(appointment.CheckIn(Start.AddMinutes(-20), VisitDay));
                    Must(appointment.Start(Start));
                    break;
                case AppointmentStatus.Completed:
                    Must(appointment.Complete(Start.AddMinutes(30)));
                    break;
                case AppointmentStatus.Cancelled:
                    Must(appointment.Cancel(CancelledBy.Admin, Start.AddDays(-1), "Doctor unavailable"));
                    break;
                case AppointmentStatus.NoShow:
                    Must(appointment.MarkNoShow(Start.AddMinutes(20)));
                    break;
                case AppointmentStatus.Rescheduled:
                    Must(appointment.RescheduleTo(Booked(), CancelledBy.Admin, Start.AddDays(-1)));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(status), status, null);
            }
            Assert.Equal(status, appointment.Status);
            return appointment;
        }

        private static void Must(Result result) => Assert.True(result.IsSuccess, string.Join(", ", result.Errors.Select(e => e.Code)));
    }
}
