using ClinicFlow.Domain.Enums;
using Domain.Entities.AppModule;
using Domain.Enums;
using static ClinicFlow.Tests.Domain.Appointments.TestAppointments;

namespace ClinicFlow.Tests.Domain.Appointments
{
    public class StartTests
    {
        [Fact]
        public void Start_RecordsTheTime()
        {
            var appointment = In(AppointmentStatus.CheckedIn);

            appointment.Start(Start.AddMinutes(-5));

            Assert.Equal(Start.AddMinutes(-5), appointment.StartedAt);
        }
    }
}
