using ClinicFlow.Domain.Enums;
using Domain.Entities.AppModule;
using Domain.Enums;
using static ClinicFlow.Tests.Domain.Appointments.TestAppointments;

namespace ClinicFlow.Tests.Domain.Appointments
{
    public class MarkNoShowTests
    {
        [Theory]
        [InlineData(14, false)]
        [InlineData(15, true)] // the grace period has just passed
        [InlineData(90, true)]
        public void MarkNoShow_OnlyAfterTheGracePeriod(int minutesAfterStart, bool allowed)
        {
            var appointment = Booked();

            var result = appointment.MarkNoShow(Start.AddMinutes(minutesAfterStart));

            if (allowed)
                Assert.Equal(Start.AddMinutes(minutesAfterStart), appointment.NoShowAt);
            else
                Assert.Equal("Appointment.NoShowTooEarly", Assert.Single(result.Errors).Code);
        }
    }
}
