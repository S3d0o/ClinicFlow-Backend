using ClinicFlow.Domain.Enums;
using Domain.Entities.AppModule;
using Domain.Enums;
using static ClinicFlow.Tests.Domain.Appointments.TestAppointments;

namespace ClinicFlow.Tests.Domain.Appointments
{
    public class AddDoctorNotesTests
    {
        [Theory]
        [InlineData(AppointmentStatus.Confirmed)]
        [InlineData(AppointmentStatus.CheckedIn)]
        [InlineData(AppointmentStatus.InProgress)]
        [InlineData(AppointmentStatus.Completed)]
        [InlineData(AppointmentStatus.NoShow)]
        public void AddDoctorNotes_ToAVisitThatIsOrWasDue_Saves(AppointmentStatus status)
        {
            var appointment = In(status);

            Assert.True(appointment.AddDoctorNotes("Follow up in 2 weeks").IsSuccess);
            Assert.Equal("Follow up in 2 weeks", appointment.DoctorNotes);
        }

        [Theory]
        [InlineData(AppointmentStatus.Pending)]
        [InlineData(AppointmentStatus.Cancelled)]
        [InlineData(AppointmentStatus.Rescheduled)]
        public void AddDoctorNotes_ToAVisitThatWontHappen_ReturnsDoctorNotesNotAllowed(AppointmentStatus status)
        {
            var appointment = In(status);

            Assert.Equal("Appointment.DoctorNotesNotAllowed", Assert.Single(appointment.AddDoctorNotes("x").Errors).Code);
            Assert.Null(appointment.DoctorNotes);
        }
    }
}
