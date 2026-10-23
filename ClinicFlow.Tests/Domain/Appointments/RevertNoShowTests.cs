using ClinicFlow.Domain.Enums;
using Domain.Entities.AppModule;
using Domain.Enums;
using static ClinicFlow.Tests.Domain.Appointments.TestAppointments;

namespace ClinicFlow.Tests.Domain.Appointments
{
    public class RevertNoShowTests
    {
        [Fact]
        public void RevertNoShow_OnTheDay_ConfirmsAgainAndClearsTheMark()
        {
            var appointment = In(AppointmentStatus.NoShow);

            appointment.RevertNoShow(VisitDay);

            Assert.Equal(AppointmentStatus.Confirmed, appointment.Status);
            Assert.Null(appointment.NoShowAt);
        }

        [Fact]
        public void RevertNoShow_OnALaterDay_ReturnsNoShowCorrectionExpired()
        {
            var appointment = In(AppointmentStatus.NoShow);

            var result = appointment.RevertNoShow(VisitDay.AddDays(1));

            Assert.Equal("Appointment.NoShowCorrectionExpired", Assert.Single(result.Errors).Code);
            Assert.Equal(AppointmentStatus.NoShow, appointment.Status);
        }
    }
}
