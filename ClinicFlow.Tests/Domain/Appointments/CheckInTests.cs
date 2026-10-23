using ClinicFlow.Domain.Enums;
using Domain.Entities.AppModule;
using Domain.Enums;
using static ClinicFlow.Tests.Domain.Appointments.TestAppointments;

namespace ClinicFlow.Tests.Domain.Appointments
{
    public class CheckInTests
    {
        [Fact]
        public void CheckIn_OnTheDay_RecordsTheTime()
        {
            var appointment = Booked();

            var result = appointment.CheckIn(Start.AddMinutes(-20), VisitDay);

            Assert.True(result.IsSuccess);
            Assert.Equal(Start.AddMinutes(-20), appointment.CheckedInAt);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(1)]
        public void CheckIn_OnAnotherDay_ReturnsCheckInNotToday(int days)
        {
            var appointment = Booked();

            var result = appointment.CheckIn(Start.AddDays(days), VisitDay.AddDays(days));

            Assert.Equal("Appointment.CheckInNotToday", Assert.Single(result.Errors).Code);
            Assert.Equal(AppointmentStatus.Confirmed, appointment.Status);
        }
    }
}
