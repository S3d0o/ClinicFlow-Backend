using ClinicFlow.Domain.Enums;
using Domain.Entities.AppModule;
using Domain.Enums;
using static ClinicFlow.Tests.Domain.Appointments.TestAppointments;

namespace ClinicFlow.Tests.Domain.Appointments
{
    public class CompleteTests
    {
        [Fact]
        public void Complete_ConfirmedBeforeItStarts_ReturnsCannotCompleteBeforeStart()
        {
            // REVIEW.md M14: completing a visit that hasn't happened would unlock a review
            var appointment = Booked();

            var result = appointment.Complete(Start.AddMinutes(-1));

            Assert.Equal("Appointment.CannotCompleteBeforeStart", Assert.Single(result.Errors).Code);
            Assert.Null(appointment.CompletedAt);
        }

        [Fact]
        public void Complete_ConfirmedAtItsStart_Completes()
            => Assert.True(Booked().Complete(Start).IsSuccess);

        [Fact]
        public void Complete_InProgressBeforeTheSlotStart_Completes()
        {
            // Seen early: the doctor started the visit, so the slot time no longer matters
            var appointment = Booked();
            appointment.CheckIn(Start.AddMinutes(-40), VisitDay);
            appointment.Start(Start.AddMinutes(-30));

            var result = appointment.Complete(Start.AddMinutes(-10));

            Assert.True(result.IsSuccess);
            Assert.Equal(Start.AddMinutes(-10), appointment.CompletedAt);
        }
    }
}
