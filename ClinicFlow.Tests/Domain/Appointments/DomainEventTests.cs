using ClinicFlow.Domain.Enums;
using Domain.Entities.AppModule;
using Domain.Enums;
using Domain.Events;
using static ClinicFlow.Tests.Domain.Appointments.TestAppointments;

namespace ClinicFlow.Tests.Domain.Appointments
{
    // Each transition records its event for the outbox (docs/v2/adr/0004 decision 5, 06 §5)
    public class DomainEventTests
    {
        private const int Id = 42;

        private static Appointment Saved(AppointmentStatus status)
        {
            var appointment = In(status);
            appointment.Id = Id;
            appointment.TakeDomainEvents(); // the events of reaching this status were saved with it
            return appointment;
        }

        [Fact]
        public void Transitions_RaiseTheirEvent()
        {
            AssertRaises(Saved(AppointmentStatus.Confirmed), a => a.CheckIn(Start.AddMinutes(-20), VisitDay), new PatientCheckedIn(Id));
            AssertRaises(Saved(AppointmentStatus.CheckedIn), a => a.Start(Start), new AppointmentStarted(Id));
            AssertRaises(Saved(AppointmentStatus.Confirmed), a => a.Complete(Start.AddMinutes(30)), new AppointmentCompleted(Id));
            AssertRaises(Saved(AppointmentStatus.Confirmed), a => a.Cancel(CancelledBy.Doctor, Start.AddDays(-1), null), new AppointmentCancelled(Id, CancelledBy.Doctor));
            AssertRaises(Saved(AppointmentStatus.Confirmed), a => a.MarkNoShow(Start.AddMinutes(20)), new AppointmentMarkedNoShow(Id));
            AssertRaises(Saved(AppointmentStatus.NoShow), a => a.RevertNoShow(VisitDay), new AppointmentNoShowReverted(Id));
            AssertRaises(Saved(AppointmentStatus.Confirmed), a => a.RescheduleTo(Booked(), CancelledBy.Admin, Start.AddDays(-1)), new AppointmentRescheduled(Id));
            AssertRaises(Saved(AppointmentStatus.Confirmed), a => a.QueueReminder(Start.AddHours(-20)), new AppointmentReminderDue(Id));
        }

        [Fact]
        public void ARefusedTransition_RaisesNothing()
        {
            var appointment = Saved(AppointmentStatus.Completed);

            appointment.Cancel(CancelledBy.Admin, Start, null);

            Assert.Empty(appointment.TakeDomainEvents());
        }

        [Fact]
        public void Book_RaisesNothing_TheServiceAddsAppointmentBookedOnceTheIdExists()
            => Assert.Empty(Booked().TakeDomainEvents());

        [Fact]
        public void TakeDomainEvents_EmptiesTheList()
        {
            // The outbox interceptor takes the events when it saves them, so a later save doesn't write them again
            var appointment = Saved(AppointmentStatus.Confirmed);
            appointment.Complete(Start.AddMinutes(30));

            Assert.Single(appointment.TakeDomainEvents());
            Assert.Empty(appointment.TakeDomainEvents());
        }

        private static void AssertRaises(Appointment appointment, Action<Appointment> transition, IDomainEvent expected)
        {
            transition(appointment);
            Assert.Equal([expected], appointment.TakeDomainEvents());
        }
    }
}
