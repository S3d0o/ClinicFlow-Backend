using Domain.Enums;
using Domain.Events;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Services.EventHandlers;

namespace ClinicFlow.Tests.Services.EventHandlerTests
{
    public class AppointmentCompletedHandlerTests : EventHandlerTestsBase
    {
        private readonly AppointmentCompletedHandler _sut;

        public AppointmentCompletedHandlerTests()
            => _sut = new AppointmentCompletedHandler(_uow, _clock, Substitute.For<ILogger<AppointmentCompletedHandler>>());

        [Fact]
        public async Task HandleAsync_TellsThePatientTheyCanReview()
        {
            StubAppointment();

            await _sut.HandleAsync(new AppointmentCompleted(AppointmentId), MessageId, _ct);

            var notification = Assert.Single(_added);
            Assert.Equal((_patientUserId, NotificationType.AppointmentCompleted, AppointmentId),
                (notification.UserId, notification.Type, notification.RelatedEntityId!.Value));
        }

        [Fact]
        public async Task HandleAsync_WhenTheAppointmentIsGone_AddsNothing()
        {
            await _sut.HandleAsync(new AppointmentCompleted(AppointmentId), MessageId, _ct);

            Assert.Empty(_added);
        }
    }
}
