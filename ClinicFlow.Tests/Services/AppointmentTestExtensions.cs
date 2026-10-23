using System.Reflection;
using Domain.Entities.AppModule;
using Domain.Enums;

namespace ClinicFlow.Tests.Services
{
    public static class AppointmentTestExtensions
    {
        private static readonly PropertyInfo StatusProperty = typeof(Appointment).GetProperty(nameof(Appointment.Status))!;

        // Puts an appointment in a status without walking the transitions, as a loaded row would be. Service tests
        // only need the state; the transitions themselves are tested in ClinicFlow.Tests/Domain/Appointments
        public static Appointment WithStatus(this Appointment appointment, AppointmentStatus status)
        {
            StatusProperty.SetValue(appointment, status);
            return appointment;
        }
    }
}
