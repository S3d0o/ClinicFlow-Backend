using ClinicFlow.Domain.Enums;
using Domain.Entities.AppModule;
using Domain.Enums;
using static ClinicFlow.Tests.Domain.Appointments.TestAppointments;

namespace ClinicFlow.Tests.Domain.Appointments
{
    public class RescheduleToTests
    {
        [Fact]
        public void RescheduleTo_LinksBothAppointments()
        {
            var original = Booked();
            var replacement = Booked();

            original.RescheduleTo(replacement, CancelledBy.Patient, Start.AddDays(-1), Window);

            Assert.Equal(AppointmentStatus.Rescheduled, original.Status);
            Assert.Same(replacement, original.RescheduledTo);
            Assert.Same(original, replacement.RescheduledFrom);
        }

        [Fact]
        public void RescheduleTo_ByPatientInsideTheWindow_ReturnsCancellationWindowExpired()
        {
            var original = Booked();
            var replacement = Booked();

            var result = original.RescheduleTo(replacement, CancelledBy.Patient, Start.AddMinutes(-60), Window);

            Assert.Equal("Appointment.CancellationWindowExpired", Assert.Single(result.Errors).Code);
            Assert.Null(replacement.RescheduledFrom);
        }

        [Fact]
        public void RescheduleTo_ByPatientWithAShorterWindow_IsAllowedAnHourBefore()
            => Assert.True(Booked().RescheduleTo(Booked(), CancelledBy.Patient, Start.AddMinutes(-60), TimeSpan.FromMinutes(30)).IsSuccess);

        [Fact]
        public void RescheduleTo_AnInvalidReplacement_Throws()
        {
            // A caller bug, not an expected failure
            var original = Booked();
            var otherPatient = Booked();
            otherPatient.PatientProfileId = PatientProfileId + 1;

            Assert.Throws<ArgumentException>(() => original.RescheduleTo(original, CancelledBy.Admin, Start.AddDays(-1), Window));
            Assert.Throws<ArgumentException>(() => original.RescheduleTo(otherPatient, CancelledBy.Admin, Start.AddDays(-1), Window));
            Assert.Throws<ArgumentException>(() => original.RescheduleTo(In(AppointmentStatus.Cancelled), CancelledBy.Admin, Start.AddDays(-1), Window));
        }
    }
}
