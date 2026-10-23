using ClinicFlow.Domain.Enums;
using Domain.Entities.AppModule;
using Domain.Enums;
using static ClinicFlow.Tests.Domain.Appointments.TestAppointments;

namespace ClinicFlow.Tests.Domain.Appointments
{
    public class BookTests
    {
        [Fact]
        public void Book_CreatesAConfirmedAppointmentForTheSlotsDoctorAndBranch()
        {
            var slot = Slot();

            var appointment = Appointment.Book(slot, PatientProfileId, "Headache", Start.AddDays(-1));

            Assert.Equal(AppointmentStatus.Confirmed, appointment.Status);
            Assert.Equal((slot.Id, slot.DoctorProfileId, slot.BranchId), (appointment.SlotId, appointment.DoctorProfileId, appointment.BranchId));
            Assert.Equal((PatientProfileId, "Headache"), (appointment.PatientProfileId, appointment.ReasonForVisit));
            Assert.Equal(Start.AddDays(-1), appointment.BookedAt);
        }
    }
}
