using Domain.Enums;
using Domain.Events;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Services.EventHandlers;

namespace ClinicFlow.Tests.Services.EventHandlerTests
{
    public class AppointmentMarkedNoShowHandlerTests : EventHandlerTestsBase
    {
        private readonly AppointmentMarkedNoShowHandler _sut;

        public AppointmentMarkedNoShowHandlerTests()
            => _sut = new AppointmentMarkedNoShowHandler(_uow, _clock, Substitute.For<ILogger<AppointmentMarkedNoShowHandler>>());

        [Fact]
        public async Task HandleAsync_TellsThePatientTheyMissedIt()
        {
            StubAppointment();

            await _sut.HandleAsync(new AppointmentMarkedNoShow(AppointmentId), MessageId, _ct);

            var notification = Assert.Single(_added);
            Assert.Equal((_patientUserId, NotificationType.SystemAlert, "Missed Appointment"),
                (notification.UserId, notification.Type, notification.Title));
        }
    }
}
