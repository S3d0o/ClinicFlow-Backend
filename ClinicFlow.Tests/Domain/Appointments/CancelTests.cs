using ClinicFlow.Domain.Enums;
using Domain.Entities.AppModule;
using Domain.Enums;
using static ClinicFlow.Tests.Domain.Appointments.TestAppointments;

namespace ClinicFlow.Tests.Domain.Appointments
{
    public class CancelTests
    {
        [Fact]
        public void Cancel_RecordsWhenWhoAndWhy()
        {
            var appointment = Booked();

            appointment.Cancel(CancelledBy.Doctor, Start.AddHours(-5), "Conference", Window);

            Assert.Equal(AppointmentStatus.Cancelled, appointment.Status);
            Assert.Equal((Start.AddHours(-5), CancelledBy.Doctor, "Conference"),
                (appointment.CancelledAt, appointment.CancelledBy, appointment.CancellationReason));
        }

        [Theory]
        [InlineData(-120, true)]  // exactly 2 hours before
        [InlineData(-121, true)]
        [InlineData(-119, false)] // a minute inside the window
        [InlineData(10, false)]   // after the start
        public void Cancel_ByPatient_OnlyUntilTwoHoursBeforeTheStart(int minutesFromStart, bool allowed)
        {
            var result = Booked().Cancel(CancelledBy.Patient, Start.AddMinutes(minutesFromStart), null, Window);

            if (allowed)
                Assert.True(result.IsSuccess);
            else
                Assert.Equal("Appointment.CancellationWindowExpired", Assert.Single(result.Errors).Code);
        }

        [Theory]
        [InlineData(0, -1, true)]     // no window: until the start
        [InlineData(0, 1, false)]
        [InlineData(24, -1440, true)] // a day's window
        [InlineData(24, -1439, false)]
        public void Cancel_ByPatient_UsesTheWindowItIsGiven(int windowHours, int minutesFromStart, bool allowed)
        {
            var result = Booked().Cancel(CancelledBy.Patient, Start.AddMinutes(minutesFromStart), null, TimeSpan.FromHours(windowHours));

            Assert.Equal(allowed, result.IsSuccess);
        }

        [Theory]
        [InlineData(CancelledBy.Doctor)]
        [InlineData(CancelledBy.Admin)]
        public void Cancel_ByStaff_HasNoWindow(CancelledBy by)
            => Assert.True(Booked().Cancel(by, Start.AddMinutes(10), null, Window).IsSuccess);

        [Fact]
        public void Cancel_WindowUsesTheUtcStart_NotTheWallClock()
        {
            // REVIEW.md M2: the 10:00 Cairo slot starts at 07:00 UTC; at 06:30 UTC it is 30 minutes away
            var result = Booked().Cancel(CancelledBy.Patient, new DateTime(2026, 10, 23, 6, 30, 0, DateTimeKind.Utc), null, Window);

            Assert.Equal("Appointment.CancellationWindowExpired", Assert.Single(result.Errors).Code);
        }
    }
}
